using ExcelDoc.Server.Localization;
using ExcelDoc.Server.Models;
using ExcelDoc.Server.Repositories.Interfaces;
using ExcelDoc.Server.Services.Interfaces;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExcelDoc.Server.Services
{
    public class ProcessamentoWorkerService : IProcessamentoWorkerService
    {
        private const string RequestPayloadKey = "RequestPayload";
        private const string ResponseBodyKey = "ResponseBody";
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly IExcelReaderService _excelReaderService;
        private readonly IJsonBuilderService _jsonBuilderService;
        private readonly IMessageService _messageService;
        private readonly IDocumentoUnicoService _documentoUnicoService;
        private readonly IAgrupamentoService _agrupamentoService;
        private readonly IProcessamentoRepository _processamentoRepository;
        private readonly ISapSessionContextAccessor _sapSessionContextAccessor;
        private readonly ISapServiceLayerClient _sapServiceLayerClient;
        private readonly ISystemClock _systemClock;
        private readonly ILicenseService _licenseService;
        private readonly ILogger<ProcessamentoWorkerService> _logger;

        public ProcessamentoWorkerService(
            IExcelReaderService excelReaderService,
            IJsonBuilderService jsonBuilderService,
            IMessageService messageService,
            IDocumentoUnicoService documentoUnicoService,
            IAgrupamentoService agrupamentoService,
            IProcessamentoRepository processamentoRepository,
            ISapSessionContextAccessor sapSessionContextAccessor,
            ISapServiceLayerClient sapServiceLayerClient,
            ISystemClock systemClock,
            ILogger<ProcessamentoWorkerService> logger,
            ILicenseService licenseService)
        {
            _excelReaderService = excelReaderService;
            _jsonBuilderService = jsonBuilderService;
            _messageService = messageService;
            _documentoUnicoService = documentoUnicoService;
            _agrupamentoService = agrupamentoService;
            _processamentoRepository = processamentoRepository;
            _sapSessionContextAccessor = sapSessionContextAccessor;
            _sapServiceLayerClient = sapServiceLayerClient;
            _systemClock = systemClock;
            _logger = logger;
            _licenseService = licenseService;
        }

        public async Task ProcessAsync(Background.ProcessamentoQueueItem item, CancellationToken cancellationToken = default)
        {
            var processamento = await _processamentoRepository.GetForExecutionAsync(item.ProcessamentoId, cancellationToken)
                ?? throw new KeyNotFoundException(_messageService.Get(MessageKeys.ProcessingNotFound));
            var sapSession = _sapSessionContextAccessor.GetRequiredSession();
            var rows = await _excelReaderService.ReadRowsAsync(item.FilePath, cancellationToken);

            if (processamento.PerfilMapeamento is null)
            {
                throw new InvalidOperationException(_messageService.Get(MessageKeys.ProcessingWithoutMappingProfileNotSupported));
            }

            IReadOnlyList<Background.ExcelDocumentGroup> groups;
            try
            {
                groups = _agrupamentoService.AgruparPorIdExcel(rows);
            }
            catch (ExcelImportValidationException ex)
            {
                await RegisterValidationErrorAsync(processamento, ex, cancellationToken);
                return;
            }

            await ProcessWithPerfilAsync(processamento, sapSession, groups, cancellationToken);

            _logger.LogInformation("Processamento {ProcessamentoId} finalizado. Sucesso={TotalSucesso} Erro={TotalErro} Ignorado={TotalIgnorado}",
                processamento.Id, processamento.TotalSucesso, processamento.TotalErro, processamento.TotalIgnorado);
        }

        private async Task ProcessWithPerfilAsync(
            Processamento processamento,
            Sap.SapSessionContext sapSession,
            IReadOnlyList<Background.ExcelDocumentGroup> groups,
            CancellationToken cancellationToken)
        {
            var perfil = processamento.PerfilMapeamento!;

            processamento.TotalRegistros = groups.Count;
            processamento.TotalErro = 0;
            processamento.TotalSucesso = 0;
            processamento.TotalIgnorado = 0;
            processamento.Status = StatusProcessamento.Processando;
            await _processamentoRepository.SaveChangesAsync(cancellationToken);

            IReadOnlyDictionary<string, int>? nfModels = null;

            foreach (var group in groups)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var groupRows = group.Rows;
                var firstRowNumber = groupRows[0].RowNumber;
                var itemLog = new ProcessamentoItem
                {
                    FK_IdProcessamento = processamento.Id,
                    IdExcel = group.IdExcel,
                    LinhaExcel = firstRowNumber,
                    JsonEnviado = string.Empty,
                    Status = StatusProcessamentoItem.Erro,
                    DataExecucao = _systemClock.UtcNow
                };

                try
                {
                    var payload = _jsonBuilderService.BuildDocumentPayload(perfil, groupRows);
                    var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
                    var idDocumentoUnico = _documentoUnicoService.BuildIdDocumentoUnico(processamento, group, payload);
                    itemLog.JsonEnviado = payloadJson;
                    itemLog.IdDocumentoUnico = idDocumentoUnico;

                    if (await _processamentoRepository.HasDocumentoProcessadoComSucessoAsync(idDocumentoUnico, cancellationToken))
                    {
                        itemLog.JsonRetorno = JsonSerializer.Serialize(new
                        {
                            Ignorado = true,
                            Motivo = _messageService.Get(MessageKeys.DocumentAlreadyImportedWithSuccess),
                            group.IdExcel,
                            IdDocumentoUnico = idDocumentoUnico
                        }, JsonOptions);
                        itemLog.Mensagem = _messageService.Get(MessageKeys.DocumentAlreadyImportedWithSuccess);
                        itemLog.Status = StatusProcessamentoItem.Ignorado;
                        processamento.TotalIgnorado++;

                        _logger.LogInformation("Documento IdExcel {IdExcel} ignorado por duplicidade no processamento {ProcessamentoId}. IdDocumentoUnico={IdDocumentoUnico}",
                            group.IdExcel, processamento.Id, idDocumentoUnico);
                    }
                    else
                    {
                        if (payload.TryGetValue("SequenceModel", out var modelValue) && modelValue is not null)
                        {
                            var modelName = modelValue.ToString()?.Trim() ?? string.Empty;
                            nfModels ??= await _sapServiceLayerClient.GetNFModelsAsync(sapSession, cancellationToken);
                            if (!nfModels.TryGetValue(modelName, out var modelCode))
                            {
                                throw new InvalidOperationException($"Código do modelo '{modelName}' não encontrado. Verifique a planilha e tente novamente.");
                            }

                            payload["SequenceModel"] = modelCode;
                            itemLog.JsonEnviado = JsonSerializer.Serialize(payload, JsonOptions);
                        }

                        var responseJson = await _sapServiceLayerClient.PostProcessamentoAsync(
                            sapSession,
                            processamento.Documento.Endpoint,
                            payload,
                            cancellationToken);

                        itemLog.JsonRetorno = responseJson;
                        itemLog.Mensagem = _messageService.Get(MessageKeys.DocumentProcessedSuccessfully);
                        itemLog.Status = StatusProcessamentoItem.Sucesso;
                        processamento.TotalSucesso++;
                    }
                }
                catch (Exception ex)
                {
                    var errorText = ex.ToString();
                    itemLog.Erro = errorText.Length > 4000 ? errorText[..4000] : errorText;
                    itemLog.JsonEnviado = itemLog.JsonEnviado == string.Empty
                        ? GetExceptionData(ex, RequestPayloadKey) ?? JsonSerializer.Serialize(new { group.IdExcel, FirstRow = firstRowNumber, RowCount = groupRows.Count }, JsonOptions)
                        : itemLog.JsonEnviado;
                    itemLog.JsonRetorno ??= GetExceptionData(ex, ResponseBodyKey) ?? ex.Message;
                    itemLog.Mensagem = ex.Message;
                    processamento.TotalErro++;

                    _logger.LogError(ex, "Erro ao processar IdExcel {IdExcel} (linha inicial {LinhaExcel}) do processamento {ProcessamentoId}",
                        group.IdExcel, firstRowNumber, processamento.Id);
                }

                itemLog.DataFinalizacao = _systemClock.UtcNow;
                await _processamentoRepository.AddItemAsync(itemLog, cancellationToken);
                await _processamentoRepository.SaveChangesAsync(cancellationToken);

                if (itemLog.Status == StatusProcessamentoItem.Sucesso)
                {
                    try
                    {
                        await _licenseService.LogRequestAsync(
                            sapSession,
                            $"Documento inserido com sucesso no SAP. Base={sapSession.Database}; Processamento={processamento.Id}; IdExcel={group.IdExcel}; IdDocumentoUnico={itemLog.IdDocumentoUnico}.",
                            cancellationToken);
                    }
                    catch (Exception exception)
                    {
                        // The document is already inserted and its success persisted. A metric
                        // failure must not change its status or trigger another SAP insertion.
                        _logger.LogWarning(
                            "Falha ao enviar métrica do processamento {ProcessamentoId}, IdExcel {IdExcel} ({ExceptionType}).",
                            processamento.Id, group.IdExcel, exception.GetType().Name);
                    }
                }
            }

            processamento.Status = processamento.TotalErro > 0 ? StatusProcessamento.Erro : StatusProcessamento.Sucesso;
            await _processamentoRepository.SaveChangesAsync(cancellationToken);
        }

        private async Task RegisterValidationErrorAsync(
            Processamento processamento,
            ExcelImportValidationException exception,
            CancellationToken cancellationToken)
        {
            processamento.Status = StatusProcessamento.Erro;
            processamento.TotalRegistros = 0;
            processamento.TotalSucesso = 0;
            processamento.TotalErro = 1;
            processamento.TotalIgnorado = 0;

            var errorText = exception.ToString();
            var now = _systemClock.UtcNow;
            await _processamentoRepository.AddItemAsync(new ProcessamentoItem
            {
                FK_IdProcessamento = processamento.Id,
                LinhaExcel = exception.RowNumber ?? 0,
                JsonEnviado = JsonSerializer.Serialize(new
                {
                    Validation = "IdExcel",
                    Column = "#",
                    exception.RowNumber
                }, JsonOptions),
                JsonRetorno = exception.Message,
                Mensagem = exception.Message,
                Erro = errorText.Length > 4000 ? errorText[..4000] : errorText,
                Status = StatusProcessamentoItem.Erro,
                DataExecucao = now,
                DataFinalizacao = now
            }, cancellationToken);

            await _processamentoRepository.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(exception, "Erro de validacao da planilha no processamento {ProcessamentoId}. LinhaExcel={LinhaExcel}",
                processamento.Id, exception.RowNumber);
        }

        private static string? GetExceptionData(Exception exception, string key)
        {
            var value = exception.Data.Contains(key)
                ? exception.Data[key]?.ToString()
                : null;

            return string.IsNullOrWhiteSpace(value)
                ? null
                : value;
        }
    }
}
