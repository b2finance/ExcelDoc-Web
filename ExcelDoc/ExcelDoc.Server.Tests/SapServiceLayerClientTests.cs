using ExcelDoc.Server.Options;
using ExcelDoc.Server.Sap;
using ExcelDoc.Server.Services;
using System.Text.Json;

namespace ExcelDoc.Server.Tests;

public sealed class SapServiceLayerClientTests
{
    [Theory]
    [InlineData("tYES", true)]
    [InlineData("tNO", false)]
    public void ParseSuperuser_ReadsSapStatus(string status, bool expected)
    {
        using var document = JsonDocument.Parse($$"""{ "value": [{ "UserCode": "alice", "Superuser": "{{status}}" }] }""");
        Assert.Equal(expected, SapServiceLayerClient.ParseSuperuser(document.RootElement, "alice"));
    }

    [Fact]
    public void ParseSuperuser_RejectsMissingStatus()
    {
        using var document = JsonDocument.Parse("{\"value\":[{\"UserCode\":\"alice\"}]}");
        Assert.Throws<InvalidOperationException>(() => SapServiceLayerClient.ParseSuperuser(document.RootElement, "alice"));
    }

    [Fact]
    public void AddNFModels_SkipsUnnamedEntriesAndReadsStringCodesAcrossPages()
    {
        var models = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using var firstPage = JsonDocument.Parse("""
            {
              "odata.metadata": "https://sap.example.test/b1s/v1/$metadata#NFModels",
              "value": [
                { "AbsEntry": "0", "NFMName": null },
                { "AbsEntry": "1", "NFMName": "Modelo 1" },
                { "AbsEntry": "2", "NFMName": "Modelo 1-A" }
              ],
              "odata.nextLink": "NFModels?$select=AbsEntry,NFMName&$skip=20"
            }
            """);
        using var secondPage = JsonDocument.Parse("""
            { "value": [{ "AbsEntry": "19", "NFMName": "Modelo 22" }] }
            """);

        SapServiceLayerClient.AddNFModels(firstPage.RootElement, models);
        SapServiceLayerClient.AddNFModels(secondPage.RootElement, models);

        Assert.Equal(3, models.Count);
        Assert.Equal(1, models["modelo 1"]);
        Assert.Equal(2, models["Modelo 1-A"]);
        Assert.Equal(19, models["Modelo 22"]);
    }

    [Fact]
    public async Task PostProcessamentoAsync_RejectsAbsoluteEndpointBeforeResolvingConnection()
    {
        using var client = CreateClient();
        var session = CreateSession();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.PostProcessamentoAsync(
                session,
                "https://untrusted.example.test/b1s/v1/Orders",
                new { }));

        Assert.Contains("caminho relativo seguro", exception.Message);
    }

    [Fact]
    public async Task PostProcessamentoAsync_RequiresLongLivedB1SlayerConnection()
    {
        using var client = CreateClient();
        var session = CreateSession();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.PostProcessamentoAsync(session, "Orders", new { }));

        Assert.Contains("B1SLayer", exception.Message);
    }

    [Fact]
    public async Task LoginAsync_HonorsAlreadyCanceledTokenWithoutCallingSap()
    {
        using var client = CreateClient();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.LoginAsync(
                "https://sap.example.test:50000/b1s/v1",
                "SBOPROD",
                "manager",
                "sap-password",
                cancellation.Token));
    }

    private static SapSessionContext CreateSession() =>
        new()
        {
            ServiceLayerBaseUrl = "https://sap.example.test:50000/b1s/v1",
            Database = "SBOPROD",
            UserName = "manager",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30)
        };

    private static SapServiceLayerClient CreateClient()
    {
        var processingOptions =
            Microsoft.Extensions.Options.Options.Create(new ProcessingOptions
            {
                SapRequestsPerSecond = 10
            });
        var sapOptions =
            Microsoft.Extensions.Options.Options.Create(new SapServiceLayerOptions
            {
                RequestTimeoutSeconds = 30
            });

        return new SapServiceLayerClient(
            new StubMessageService(),
            processingOptions,
            sapOptions);
    }
}
