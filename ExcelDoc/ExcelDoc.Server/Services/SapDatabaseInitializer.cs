using System.Collections.Concurrent;
using System.Net;
using ExcelDoc.Server.Models;
using ExcelDoc.Server.Sap;
using ExcelDoc.Server.Services.Interfaces;

namespace ExcelDoc.Server.Services;

public sealed class SapDatabaseInitializer : ISapDatabaseInitializer
{
    private const int CurrentSchemaVersion = 2;
    private const int CurrentSeedVersion = 4;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> InstallationLocks =
        new(StringComparer.OrdinalIgnoreCase);
    private const string HeaderCollection = "Cabeçalho Documentos de Marketing";
    private const string HeaderMapping = "Mapeamento Padrão - Cabeçalho";
    private const string LinesMapping = "Mapeamento Padrão - DocumentLines";
    private const string InstallmentsMapping = "Mapeamento Padrão - DocumentInstallments";
    private const string MarketingProfile = "Documentos de Marketing";

    private static readonly DocumentSeed[] Documents =
    [
        new("Nota Fiscal de Entrada", "PurchaseInvoices"),
        new("Nota Fiscal de Saída", "Invoices"),
        new("Pedido de Venda", "Orders"),
        new("Pedido de Compra", "PurchaseOrders"),
        new("Adiantamento de Fornecedor", "PurchaseDownPayments"),
        new("Adiantamento de Cliente", "DownPayments"),
        new("Oferta de Compra", "PurchaseQuotations"),
        new("Solicitação de Compra", "PurchaseRequests"),
        new("Recebimento de mercadorias", "PurchaseDeliveryNotes"),
        new("Dev. Nota Fiscal Entrada", "PurchaseCreditNotes"),
        new("Dev. Nota Fiscal de Saída", "CreditNotes"),
        new("Devolução de mercadorias", "PurchaseReturns"),
        new("Pedido de Devolução de Mercadorias", "GoodsReturnRequest"),
        new("Cotação de Vendas", "Quotations"),
        new("Entrega", "DeliveryNotes"),
        new("Devoluções", "Returns"),
        new("Pedido de Devolução", "ReturnRequest"),
        new("Entrada de Mercadorias", "InventoryGenEntries"),
        new("Pedido de Transferência de Estoque", "InventoryTransferRequests"),
        new("Transferência do Estoque", "StockTransfers"),
        new("Saída de Mercadorias", "InventoryGenExits")
    ];

    private static readonly string[] MarketingDocumentEndpoints =
    [
        "PurchaseInvoices",
        "Invoices",
        "Orders",
        "PurchaseOrders",
        "PurchaseDownPayments",
        "DownPayments",
        "PurchaseQuotations",
        "PurchaseRequests",
        "PurchaseDeliveryNotes",
        "PurchaseCreditNotes",
        "CreditNotes",
        "PurchaseReturns",
        "GoodsReturnRequest",
        "Quotations",
        "DeliveryNotes",
        "Returns",
        "ReturnRequest"
    ];

    private static readonly CollectionSeed[] Collections =
    [
        new(HeaderCollection, "Document: campos do cabeçalho do documento.", TipoColecao.Header),
        new("DocumentLines", "Linhas do documento de marketing.", TipoColecao.Line),
        new("DocumentInstallments", "Parcelas do documento.", TipoColecao.Line),
        new("DocumentAdditionalExpenses", "Despesas adicionais do documento.", TipoColecao.Line),
        new("DocumentLineAdditionalExpenses", "Despesas adicionais vinculadas a uma DocumentLine.", TipoColecao.Line),
        new("DocumentSpecialLines", "Linhas especiais do documento.", TipoColecao.Line),
        new("DocumentLinesBinAllocations", "Alocações de posição vinculadas a uma DocumentLine.", TipoColecao.Line),
        new("BatchNumbers", "Lotes vinculados a uma DocumentLine.", TipoColecao.Line),
        new("SerialNumbers", "Números de série vinculados a uma DocumentLine.", TipoColecao.Line),
        new("WithholdingTaxDataCollection", "Dados de imposto retido do documento.", TipoColecao.Line),
        new("WithholdingTaxDataWTXCollection", "Dados WTX de imposto retido do documento.", TipoColecao.Line)
    ];

    private static readonly MappingSeed[] Mappings =
    [
        new(
            HeaderMapping,
            HeaderCollection,
            [
                new("DocDate", "Data de Lançamento", 2, TipoCampo.DateTime, "yyyy-MM-dd"),
                new("TaxDate", "Data de emissão", 3, TipoCampo.DateTime, "yyyy-MM-dd"),
                new("DocDueDate", "Data de vencimento", 4, TipoCampo.DateTime, "yyyy-MM-dd"),
                new("BPL_IDAssignedToInvoice", "Código da filial", 5, TipoCampo.Int, null),
                new("CardCode", "Código do parceiro de negócios", 6, TipoCampo.String, null),
                new("CardName", "Nome do parceiro de negócios", 7, TipoCampo.String, null),
                new("ImportFileNum", "Pedido", 8, TipoCampo.Int, null),
                new("SequenceCode", "Sequência do documento", 9, TipoCampo.Int, null),
                new("SequenceSerial", "Número da nota fiscal", 10, TipoCampo.Int, null),
                new("SeriesString", "Série do documento", 11, TipoCampo.String, null),
                new("SequenceModel", "Modelo do documento", 12, TipoCampo.String, null),
                new("Comments", "Observações", 13, TipoCampo.String, null),
                new("U_TX_DctfReceita", "DCTF Receita", 35, TipoCampo.String, null),
                new("U_ChaveAcesso", "Chave de acesso da nota fiscal", 39, TipoCampo.String, null)
            ]),
        new(
            LinesMapping,
            "DocumentLines",
            [
                new("ItemCode", "Código do item", 14, TipoCampo.String, null),
                new("Quantity", "Quantidade", 15, TipoCampo.Double, null),
                new("Price", "Preço unitário", 16, TipoCampo.Double, null),
                new("Usage", "Utilização", 17, TipoCampo.String, null),
                new("TaxCode", "Código do imposto", 18, TipoCampo.String, null),
                new("WTLiable", "Sujeito a imposto de renda retido na fonte", 19, TipoCampo.Boolean, null),
                new("CostingCode", "Centro de custo 1", 20, TipoCampo.String, null),
                new("CostingCode2", "Centro de custo 2", 21, TipoCampo.String, null),
                new("CostingCode3", "Centro de custo 3", 22, TipoCampo.String, null),
                new("CostingCode4", "Centro de custo 4", 23, TipoCampo.String, null),
                new("CostingCode5", "Centro de custo 5", 24, TipoCampo.String, null),
                new("AccountCode", "Conta do razão", 25, TipoCampo.String, null),
                new("WarehouseCode", "Código do depósito", 26, TipoCampo.String, null),
                new("ProjectCode", "Projeto", 27, TipoCampo.String, null),
                new("Incoterms", "Incoterms", 28, TipoCampo.String, null)
            ]),
        new(
            InstallmentsMapping,
            "DocumentInstallments",
            [
                new("U_CodigoDeBarras", "Código de barras", 33, TipoCampo.String, null),
                new("U_IB_TipoImposto", "Tipo de imposto", 34, TipoCampo.String, null),
                new("U_IB_LinhaDigitavel", "Linha digitável", 36, TipoCampo.String, null),
                new("U_IB_QrCodePix", "QR Code do Pix", 37, TipoCampo.String, null),
                new("U_IB_TipoTransfRealizar", "Tipo de transferência", 38, TipoCampo.String, null)
            ]),
        EmptyMapping("DocumentAdditionalExpenses"),
        EmptyMapping("DocumentLineAdditionalExpenses"),
        EmptyMapping("DocumentSpecialLines"),
        EmptyMapping("DocumentLinesBinAllocations"),
        new(
            DefaultMappingName("BatchNumbers"),
            "BatchNumbers",
            [
                new("BatchNumber", "Número do lote", 29, TipoCampo.String, null),
                new("Quantity", "Quantidade do lote", 30, TipoCampo.Double, null)
            ]),
        EmptyMapping("SerialNumbers"),
        new(
            DefaultMappingName("WithholdingTaxDataCollection"),
            "WithholdingTaxDataCollection",
            [
                new("TaxableAmount", "Valor sujeito a imposto retido", 31, TipoCampo.Double, null),
                new("WTCode", "Código do imposto retido", 32, TipoCampo.String, null)
            ]),
        EmptyMapping("WithholdingTaxDataWTXCollection")
    ];

    private static readonly ProfileItemSeed[] ProfileItems =
    [
        new(HeaderCollection, HeaderMapping),
        new("DocumentLines", LinesMapping),
        new("DocumentInstallments", InstallmentsMapping),
        new("DocumentAdditionalExpenses", DefaultMappingName("DocumentAdditionalExpenses")),
        new("DocumentLineAdditionalExpenses", DefaultMappingName("DocumentLineAdditionalExpenses"), "DocumentLines"),
        new("DocumentSpecialLines", DefaultMappingName("DocumentSpecialLines")),
        new("DocumentLinesBinAllocations", DefaultMappingName("DocumentLinesBinAllocations"), "DocumentLines"),
        new("BatchNumbers", DefaultMappingName("BatchNumbers"), "DocumentLines"),
        new("SerialNumbers", DefaultMappingName("SerialNumbers"), "DocumentLines"),
        new("WithholdingTaxDataCollection", DefaultMappingName("WithholdingTaxDataCollection")),
        new("WithholdingTaxDataWTXCollection", DefaultMappingName("WithholdingTaxDataWTXCollection"))
    ];

    private readonly ILogger<SapDatabaseInitializer> _logger;
    private readonly SapSchemaInstaller _schemaInstaller;
    private readonly ISapUdtStore _store;
    private readonly ISapServiceLayerClient _sapClient;

    public SapDatabaseInitializer(
        ILogger<SapDatabaseInitializer> logger,
        SapSchemaInstaller schemaInstaller,
        ISapUdtStore store,
        ISapServiceLayerClient sapClient)
    {
        _logger = logger;
        _schemaInstaller = schemaInstaller;
        _store = store;
        _sapClient = sapClient;
    }

    public async Task InitializeAsync(
        SapSessionContext session,
        CancellationToken cancellationToken = default)
    {
        var lockKey = $"{session.ServiceLayerBaseUrl}\n{session.Database}";
        var installationLock = InstallationLocks.GetOrAdd(
            lockKey,
            _ => new SemaphoreSlim(1, 1));

        await installationLock.WaitAsync(cancellationToken);
        try
        {
            await InitializeCoreAsync(
                session,
                cancellationToken);
        }
        finally
        {
            installationLock.Release();
        }
    }

    private async Task InitializeCoreAsync(
        SapSessionContext session,
        CancellationToken cancellationToken)
    {
        var installationState = await TryGetInstallationStateAsync(cancellationToken);
        var seedRequired = false;
        var schemaUpdated = false;
        var installedSchemaVersion =
            installationState?.GetInt("SchemaVersion") ?? 0;
        var installedSeedVersion =
            installationState?.GetInt("SeedVersion") ?? 0;

        if (installedSchemaVersion > CurrentSchemaVersion ||
            installedSeedVersion > CurrentSeedVersion)
        {
            throw new InvalidOperationException(
                "A base foi preparada por uma versão mais nova do ExcelDoc.");
        }

        var allowSchemaCreation = installedSchemaVersion < CurrentSchemaVersion ||
            installedSeedVersion < CurrentSeedVersion;
        if (allowSchemaCreation)
        {
            if (!await _sapClient.IsSuperuserAsync(session, cancellationToken))
                throw new InvalidOperationException(
                    "A estrutura do ExcelDoc ainda não foi instalada ou atualizada nesta base. " +
                    "Entre com um superusuário do SAP Business One.");
        }

        if (allowSchemaCreation)
        {
            if (installedSchemaVersion < CurrentSchemaVersion)
            {
                var result = await _schemaInstaller.EnsureCreatedAsync(cancellationToken);
                schemaUpdated = true;
                _logger.LogInformation(
                    "Metadados ExcelDoc atualizados na base {Database}. Tabelas criadas={Tables}; campos criados={Fields}; chaves criadas={Keys}.",
                    session.Database,
                    result.CreatedTables,
                    result.CreatedFields,
                    result.CreatedKeys);
                installationState = await TryGetInstallationStateAsync(cancellationToken);
            }

            seedRequired = installedSeedVersion < CurrentSeedVersion;
        }

        if (allowSchemaCreation && seedRequired)
        {
            var documents = await EnsureDocumentsAsync(cancellationToken);
            var collections = await EnsureCollectionsAsync(cancellationToken);
            await EnsureDocumentCollectionLinksAsync(documents, collections, cancellationToken);
            var mappings = await EnsureMappingsAsync(collections, cancellationToken);
            await EnsureProfilesAsync(documents, collections, mappings, cancellationToken);
            await SaveInstallationStateAsync(installationState, cancellationToken);
        }
        else if (allowSchemaCreation && schemaUpdated)
        {
            await SaveInstallationStateAsync(installationState, cancellationToken);
        }

    }

    private async Task<SapUdtRecord?> TryGetInstallationStateAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return (await _store.QueryAsync(
                    SapUdtSchema.Schema,
                    orderBy: "Code asc",
                    top: 1,
                    cancellationToken: cancellationToken))
                .FirstOrDefault();
        }
        catch (SapServiceLayerException exception)
            when (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return null;
        }
    }

    private async Task SaveInstallationStateAsync(
        SapUdtRecord? installationState,
        CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, object?>
        {
            ["SchemaVersion"] = CurrentSchemaVersion,
            ["SeedVersion"] = CurrentSeedVersion
        };

        if (installationState is null)
        {
            await _store.AddAsync(
                SapUdtSchema.Schema,
                fields,
                name: "ExcelDoc",
                cancellationToken: cancellationToken);
            return;
        }

        await _store.UpdateAsync(
            SapUdtSchema.Schema,
            installationState.Id,
            fields,
            cancellationToken);
    }

    private async Task<Dictionary<string, Documento>> EnsureDocumentsAsync(
        CancellationToken cancellationToken)
    {
        var existing = (await _store.QueryAsync(
                SapUdtSchema.Documento,
                cancellationToken: cancellationToken))
            .Select(SapEntityMapper.ToDocumento)
            .GroupBy(document => document.Endpoint, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(document => document.Id).First(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var seed in Documents)
        {
            if (existing.ContainsKey(seed.Endpoint))
            {
                continue;
            }

            var document = new Documento
            {
                NomeDocumento = seed.Name,
                Endpoint = seed.Endpoint
            };
            document.Id = await _store.AddAsync(
                SapUdtSchema.Documento,
                SapEntityMapper.Fields(document),
                cancellationToken: cancellationToken);
            existing[seed.Endpoint] = document;
        }

        return existing;
    }

    private async Task<Dictionary<string, Colecao>> EnsureCollectionsAsync(
        CancellationToken cancellationToken)
    {
        var existing = (await _store.QueryAsync(
                SapUdtSchema.Colecao,
                cancellationToken: cancellationToken))
            .Select(SapEntityMapper.ToColecao)
            .GroupBy(collection => collection.NomeColecao, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(collection => collection.Id).First(),
                StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, Colecao>(StringComparer.OrdinalIgnoreCase);

        foreach (var seed in Collections)
        {
            if (!existing.TryGetValue(seed.Name, out var collection))
            {
                collection = new Colecao
                {
                    NomeColecao = seed.Name,
                    Descricao = seed.Description,
                    TipoColecao = seed.Type,
                    IsPadrao = true
                };
                collection.Id = await _store.AddAsync(
                    SapUdtSchema.Colecao,
                    SapEntityMapper.Fields(collection),
                    cancellationToken: cancellationToken);
                existing[seed.Name] = collection;
            }
            else if (!collection.IsPadrao)
            {
                collection.IsPadrao = true;
                await _store.UpdateAsync(
                    SapUdtSchema.Colecao,
                    collection.Id,
                    new Dictionary<string, object?>
                    {
                        ["IsPadrao"] = "Y"
                    },
                    cancellationToken);
            }

            result[seed.Name] = collection;
        }

        return result;
    }

    private async Task EnsureDocumentCollectionLinksAsync(
        IReadOnlyDictionary<string, Documento> documents,
        IReadOnlyDictionary<string, Colecao> collections,
        CancellationToken cancellationToken)
    {
        var existing = (await _store.QueryAsync(
                SapUdtSchema.DocumentoColecao,
                cancellationToken: cancellationToken))
            .Select(SapEntityMapper.ToDocumentoColecao)
            .Select(link => (link.FK_IdDocumento, link.FK_IdColecao))
            .ToHashSet();

        foreach (var endpoint in MarketingDocumentEndpoints)
        {
            foreach (var collection in collections.Values)
            {
                var key = (documents[endpoint].Id, collection.Id);
                if (existing.Contains(key))
                {
                    continue;
                }

                await _store.AddAsync(
                    SapUdtSchema.DocumentoColecao,
                    SapEntityMapper.Fields(new DocumentoColecao
                    {
                        FK_IdDocumento = key.Item1,
                        FK_IdColecao = key.Item2
                    }),
                    cancellationToken: cancellationToken);
                existing.Add(key);
            }
        }
    }

    private async Task<Dictionary<string, Mapeamento>> EnsureMappingsAsync(
        IReadOnlyDictionary<string, Colecao> collections,
        CancellationToken cancellationToken)
    {
        var existingMappings = (await _store.QueryAsync(
                SapUdtSchema.Mapeamento,
                cancellationToken: cancellationToken))
            .Select(SapEntityMapper.ToMapeamento)
            .Where(mapping => mapping.IsPadraoGlobal)
            .ToList();
        var existingFields = (await _store.QueryAsync(
                SapUdtSchema.MapeamentoCampo,
                cancellationToken: cancellationToken))
            .Select(SapEntityMapper.ToMapeamentoCampo)
            .GroupBy(field => field.FK_IdMapeamento)
            .ToDictionary(group => group.Key, group => group.ToList());
        var result = new Dictionary<string, Mapeamento>(StringComparer.OrdinalIgnoreCase);

        foreach (var seed in Mappings)
        {
            var collection = collections[seed.CollectionName];
            var mapping = existingMappings.FirstOrDefault(value =>
                string.Equals(value.Nome, seed.Name, StringComparison.OrdinalIgnoreCase));

            if (mapping is null)
            {
                mapping = new Mapeamento
                {
                    Nome = seed.Name,
                    FK_IdColecao = collection.Id,
                    IsPadrao = true,
                    DataCriacao = DateTime.UtcNow
                };
                mapping.Id = await _store.AddAsync(
                    SapUdtSchema.Mapeamento,
                    SapEntityMapper.Fields(mapping),
                    cancellationToken: cancellationToken);
                existingMappings.Add(mapping);
            }

            var fields = existingFields.GetValueOrDefault(mapping.Id) ?? [];
            if (seed.Fields.Count > 0 && !FieldsMatchSeed(fields, seed.Fields))
            {
                foreach (var field in fields)
                {
                    await _store.DeleteAsync(
                        SapUdtSchema.MapeamentoCampo,
                        field.Id,
                        cancellationToken);
                }

                fields = [];
                foreach (var fieldSeed in seed.Fields)
                {
                    var field = new MapeamentoCampo
                    {
                        FK_IdMapeamento = mapping.Id,
                        NomeCampo = fieldSeed.Name,
                        DescricaoCampo = fieldSeed.Description,
                        IndiceColuna = fieldSeed.ColumnIndex,
                        TipoCampo = fieldSeed.Type,
                        Formato = fieldSeed.Format,
                        Ativo = true
                    };
                    field.Id = await _store.AddAsync(
                        SapUdtSchema.MapeamentoCampo,
                        SapEntityMapper.Fields(field),
                        cancellationToken: cancellationToken);
                    fields.Add(field);
                }
            }

            mapping.Campos = fields;
            existingFields[mapping.Id] = fields;
            result[seed.Name] = mapping;
        }

        return result;
    }

    private async Task EnsureProfilesAsync(
        IReadOnlyDictionary<string, Documento> documents,
        IReadOnlyDictionary<string, Colecao> collections,
        IReadOnlyDictionary<string, Mapeamento> mappings,
        CancellationToken cancellationToken)
    {
        var profiles = (await _store.QueryAsync(
                SapUdtSchema.PerfilMapeamento,
                cancellationToken: cancellationToken))
            .Select(SapEntityMapper.ToPerfilMapeamento)
            .ToList();

        foreach (var endpoint in MarketingDocumentEndpoints)
        {
            var document = documents[endpoint];
            var profile = profiles.FirstOrDefault(value =>
                value.FK_IdDocumento == document.Id &&
                value.IsPadraoGlobal &&
                string.Equals(value.Nome, MarketingProfile, StringComparison.OrdinalIgnoreCase));
            if (profile is null)
            {
                profile = new PerfilMapeamento
                {
                    Nome = MarketingProfile,
                    FK_IdDocumento = document.Id,
                    IsPadrao = true,
                    DataCriacao = DateTime.UtcNow
                };
                profile.Id = await _store.AddAsync(
                    SapUdtSchema.PerfilMapeamento,
                    SapEntityMapper.Fields(profile),
                    cancellationToken: cancellationToken);
                profiles.Add(profile);
            }

            var items = (await _store.QueryAsync(
                    SapUdtSchema.PerfilMapeamentoItem,
                    filter: SapOData.Eq("PerfilId", profile.Id),
                    cancellationToken: cancellationToken))
                .Select(SapEntityMapper.ToPerfilMapeamentoItem)
                .ToList();
            var byCollection = items.ToDictionary(item => item.FK_IdColecao);
            var createdItemIds = new HashSet<int>();

            foreach (var seed in ProfileItems)
            {
                var collection = collections[seed.CollectionName];
                var mapping = mappings[seed.MappingName];
                if (!byCollection.TryGetValue(collection.Id, out var item))
                {
                    item = new PerfilMapeamentoItem
                    {
                        FK_IdPerfilMapeamento = profile.Id,
                        FK_IdColecao = collection.Id,
                        FK_IdMapeamento = mapping.Id
                    };
                    item.Id = await _store.AddAsync(
                        SapUdtSchema.PerfilMapeamentoItem,
                        SapEntityMapper.Fields(item),
                        cancellationToken: cancellationToken);
                    items.Add(item);
                    byCollection[collection.Id] = item;
                    createdItemIds.Add(item.Id);
                }
            }

            foreach (var seed in ProfileItems)
            {
                var item = byCollection[collections[seed.CollectionName].Id];
                if (!createdItemIds.Contains(item.Id))
                {
                    continue;
                }

                item.FK_IdPerfilMapeamentoItemPai = seed.ParentCollectionName is null
                    ? null
                    : byCollection[collections[seed.ParentCollectionName].Id].Id;
                await _store.UpdateAsync(
                    SapUdtSchema.PerfilMapeamentoItem,
                    item.Id,
                    SapEntityMapper.Fields(item),
                    cancellationToken);
            }
        }
    }

    private static MappingSeed EmptyMapping(string collectionName) =>
        new(DefaultMappingName(collectionName), collectionName, []);

    private static bool FieldsMatchSeed(
        IReadOnlyCollection<MapeamentoCampo> fields,
        IReadOnlyCollection<MappingFieldSeed> fieldSeeds)
    {
        if (fields.Count != fieldSeeds.Count)
        {
            return false;
        }

        return fieldSeeds.All(seed =>
            fields.Any(field =>
                string.Equals(field.NomeCampo, seed.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(field.DescricaoCampo, seed.Description, StringComparison.Ordinal) &&
                field.IndiceColuna == seed.ColumnIndex &&
                field.TipoCampo == seed.Type &&
                string.Equals(field.Formato, seed.Format, StringComparison.Ordinal) &&
                field.Ativo));
    }

    private static string DefaultMappingName(string collectionName) =>
        $"Mapeamento Padrão - {collectionName}";

    private sealed record DocumentSeed(string Name, string Endpoint);

    private sealed record CollectionSeed(
        string Name,
        string Description,
        TipoColecao Type);

    private sealed record MappingSeed(
        string Name,
        string CollectionName,
        IReadOnlyCollection<MappingFieldSeed> Fields);

    private sealed record MappingFieldSeed(
        string Name,
        string Description,
        int ColumnIndex,
        TipoCampo Type,
        string? Format);

    private sealed record ProfileItemSeed(
        string CollectionName,
        string MappingName,
        string? ParentCollectionName = null);
}
