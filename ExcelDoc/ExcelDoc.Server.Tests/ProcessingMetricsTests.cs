using ExcelDoc.Server.Background;
using ExcelDoc.Server.Models;
using ExcelDoc.Server.Repositories.Interfaces;
using ExcelDoc.Server.Sap;
using ExcelDoc.Server.Services;
using ExcelDoc.Server.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExcelDoc.Server.Tests;

public sealed class ProcessingMetricsTests
{
    [Theory]
    [InlineData(false, false, false, false, 1, StatusProcessamentoItem.Sucesso)]
    [InlineData(true, false, false, false, 0, StatusProcessamentoItem.Erro)]
    [InlineData(false, true, false, false, 0, StatusProcessamentoItem.Ignorado)]
    [InlineData(false, false, true, false, 1, StatusProcessamentoItem.Sucesso)]
    [InlineData(false, false, false, true, 0, StatusProcessamentoItem.Erro)]
    public async Task SendsOnlyForInsertedDocumentsAndPreservesSuccessOnMetricFailure(
        bool sapFails, bool duplicate, bool logFails, bool payloadFails,
        int expectedLogs, StatusProcessamentoItem expectedStatus)
    {
        var repo = new Repository { Duplicate = duplicate };
        var sap = new SapClient { Fail = sapFails };
        var session = new SapSessionContext { Database = "customer-db" };
        var metrics = new Metrics(repo, session) { Fail = logFails };
        var service = CreateWorker(repo, sap, session, metrics, payloadFails);
        await service.ProcessAsync(new ProcessamentoQueueItem { ProcessamentoId = 1 });
        Assert.Equal(expectedLogs, metrics.Calls);
        Assert.True(metrics.ValidCalls);
        Assert.Equal(expectedStatus, Assert.Single(repo.Items).Status);
        Assert.Equal(expectedStatus == StatusProcessamentoItem.Sucesso ? 1 : 0, repo.Processing.TotalSucesso);
        Assert.Equal(expectedStatus == StatusProcessamentoItem.Erro ? 1 : 0, repo.Processing.TotalErro);
        Assert.Equal(duplicate || payloadFails ? 0 : 1, sap.Calls);
    }

    [Fact]
    public async Task MixedBatchLogsOnlySuccessfulDocumentsAndContinuesAfterLogFailure()
    {
        var repo = new Repository();
        var sap = new SapClient { FailSecond = true };
        var session = new SapSessionContext();
        var metrics = new Metrics(repo, session) { Fail = true };
        var worker = CreateWorker(repo, sap, session, metrics, count: 3);
        await worker.ProcessAsync(new ProcessamentoQueueItem { ProcessamentoId = 1 });
        Assert.Equal(2, metrics.Calls);
        Assert.True(metrics.ValidCalls);
        Assert.Equal(3, sap.Calls);
        Assert.Equal(2, repo.Processing.TotalSucesso);
        Assert.Equal(1, repo.Processing.TotalErro);
        Assert.Equal(3, repo.Items.Count);
    }

    [Fact]
    public async Task InvalidSpreadsheetDoesNotSendMetrics()
    {
        var repo = new Repository();
        var sap = new SapClient();
        var session = new SapSessionContext();
        var metrics = new Metrics(repo, session);
        await CreateWorker(repo, sap, session, metrics, count: 0)
            .ProcessAsync(new ProcessamentoQueueItem { ProcessamentoId = 1 });
        Assert.Equal(0, metrics.Calls);
        Assert.Equal(0, sap.Calls);
        Assert.Equal(1, repo.Processing.TotalErro);
    }

    [Fact]
    public async Task SequenceModel_ResolvesOnceAndContinuesAfterUnknownName()
    {
        var repo = new Repository();
        var sap = new SapClient
        {
            Models = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Modelo 55"] = 55 }
        };
        var session = new SapSessionContext();
        var metrics = new Metrics(repo, session);
        var worker = CreateWorker(repo, sap, session, metrics, count: 3,
            modelNames: ["Modelo 55", "Desconhecido", "modelo 55"]);

        await worker.ProcessAsync(new ProcessamentoQueueItem { ProcessamentoId = 1 });

        Assert.Equal(1, sap.ModelCalls);
        Assert.Equal(2, sap.Calls);
        Assert.Equal(2, repo.Processing.TotalSucesso);
        Assert.Equal(1, repo.Processing.TotalErro);
        Assert.Equal([StatusProcessamentoItem.Sucesso, StatusProcessamentoItem.Erro, StatusProcessamentoItem.Sucesso],
            repo.Items.Select(item => item.Status));
        Assert.Contains("Desconhecido", repo.Items[1].Mensagem);
        Assert.All(sap.SentModels, code => Assert.Equal(55, code));
        Assert.Contains("\"SequenceModel\":55", repo.Items[0].JsonEnviado);
    }

    private static ProcessamentoWorkerService CreateWorker(Repository repo, SapClient sap,
        SapSessionContext session, Metrics metrics, bool payloadFails = false, int count = 1,
        string[]? modelNames = null) => new(
        new Reader(count), new Builder(payloadFails, modelNames), new StubMessageService(), new DocumentoUnicoService(),
        new AgrupamentoService(new StubMessageService()), repo, new Accessor(session), sap,
        new Clock(), NullLogger<ProcessamentoWorkerService>.Instance, metrics);

    private sealed class Metrics(Repository repository, SapSessionContext expectedSession) : ILicenseService
    {
        public int Calls;
        public bool ValidCalls = true;
        public bool Fail;
        public Task ValidateAsync(SapSessionContext session, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task LogRequestAsync(SapSessionContext session, string message, CancellationToken cancellationToken = default)
        {
            Calls++;
            ValidCalls &= ReferenceEquals(expectedSession, session)
                && repository.Items.LastOrDefault()?.Status == StatusProcessamentoItem.Sucesso
                && repository.LastItemSaved
                && message.Contains("Documento inserido com sucesso");
            if (Fail) throw new HttpRequestException("metrics unavailable");
            return Task.CompletedTask;
        }
    }
    private sealed class Clock : ISystemClock { public DateTime UtcNow => DateTime.UtcNow; }
    private sealed class Builder(bool fail, string[]? modelNames) : IJsonBuilderService
    {
        public IDictionary<string, object?> BuildDocumentPayload(PerfilMapeamento perfil, IReadOnlyList<ExcelRowData> groupRows) =>
            fail ? throw new InvalidOperationException("invalid payload") : Build(groupRows[0].RowNumber);

        private IDictionary<string, object?> Build(int rowNumber)
        {
            var payload = new Dictionary<string, object?> { ["Id"] = rowNumber };
            if (modelNames is not null) payload["SequenceModel"] = modelNames[rowNumber - 2];
            return payload;
        }
    }
    private sealed class Reader(int count) : IExcelReaderService
    {
        public Task<IReadOnlyList<string>> ReadFirstRowAsync(Stream stream, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<ExcelRowData>> ReadRowsAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var rows = new List<ExcelRowData> { new() { RowNumber = 1, Values = new Dictionary<int, string?> { [1] = "#" } } };
            rows.AddRange(Enumerable.Range(1, count).Select(i => new ExcelRowData { RowNumber = i + 1, Values = new Dictionary<int, string?> { [1] = i.ToString() } }));
            return Task.FromResult<IReadOnlyCollection<ExcelRowData>>(rows);
        }
    }
    private sealed class Accessor(SapSessionContext session) : ISapSessionContextAccessor
    {
        public SapSessionContext GetRequiredSession() => session;
        public string GetRequiredSessionKey() => session.SessionKey;
        public void SetSessionKey(string sessionKey) => throw new NotSupportedException();
        public void SetJobSessionKey(string sessionKey) => throw new NotSupportedException();
    }
    private sealed class SapClient : ISapServiceLayerClient
    {
        public IReadOnlyDictionary<string, int> Models = new Dictionary<string, int>();
        public int ModelCalls;
        public List<int> SentModels { get; } = [];
        public Task<IReadOnlyDictionary<string, int>> GetNFModelsAsync(SapSessionContext session, CancellationToken cancellationToken = default)
        {
            ModelCalls++;
            return Task.FromResult(Models);
        }
        public bool Fail;
        public bool FailSecond;
        public int Calls;
        public Task<string> PostProcessamentoAsync(SapSessionContext session, string endpoint, object payload, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (payload is IDictionary<string, object?> fields && fields.TryGetValue("SequenceModel", out var model))
                SentModels.Add(Assert.IsType<int>(model));
            return Fail || (FailSecond && Calls == 2) ? Task.FromException<string>(new InvalidOperationException("SAP error")) : Task.FromResult("{\"DocEntry\":123}");
        }
        public Task<string> GetInstallationNumberAsync(SapSessionContext session, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SapSessionContext> LoginAsync(string database, string userName, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task LogoutAsync(SapSessionContext session, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Repository : IProcessamentoRepository
    {
        public bool Duplicate;
        public bool LastItemSaved;
        public List<ProcessamentoItem> Items { get; } = [];
        public Processamento Processing { get; } = new() { Id = 1, Documento = new Documento { Endpoint = "Orders" }, PerfilMapeamento = new PerfilMapeamento() };
        public Task<Processamento?> GetForExecutionAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult<Processamento?>(Processing);
        public Task<bool> HasDocumentoProcessadoComSucessoAsync(string idDocumentoUnico, CancellationToken cancellationToken = default) => Task.FromResult(Duplicate);
        public Task AddItemAsync(ProcessamentoItem item, CancellationToken cancellationToken = default) { Items.Add(item); LastItemSaved = false; return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) { LastItemSaved = true; return Task.CompletedTask; }
        public Task AddAsync(Processamento processamento, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Processamento?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(IReadOnlyCollection<Processamento> Items, int TotalCount)> GetPagedAsync(StatusProcessamento? status, int pageNumber, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(IReadOnlyCollection<ProcessamentoItem> Items, int TotalCount)> GetItemsPagedAsync(int processamentoId, StatusProcessamentoItem? status, bool apenasComErro, int pageNumber, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
