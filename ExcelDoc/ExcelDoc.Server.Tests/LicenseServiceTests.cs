using System.Net;
using ExcelDoc.Server.Options;
using ExcelDoc.Server.Sap;
using ExcelDoc.Server.Services;
using ExcelDoc.Server.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExcelDoc.Server.Tests;

public sealed class LicenseServiceTests
{
    [Theory]
    [InlineData("{\"serial\":\"license-42\",\"active\":true,\"dueDate\":\"2099-01-01T00:00:00Z\"}", true)]
    [InlineData("{\"serial\":\"license-42\",\"active\":false,\"dueDate\":\"2099-01-01T00:00:00Z\"}", false)]
    [InlineData("{\"serial\":\"license-42\",\"active\":true,\"dueDate\":\"2000-01-01T00:00:00Z\"}", false)]
    [InlineData("null", false)]
    [InlineData("{}", false)]
    public async Task ValidatesActiveAndUnexpiredLicense(string body, bool allowed)
    {
        using var handler = new ApiHandler { LicenseBody = body };
        using var service = CreateService(handler);
        if (allowed) await service.ValidateAsync(new SapSessionContext());
        else await Assert.ThrowsAsync<LicenseValidationException>(() => service.ValidateAsync(new SapSessionContext()));
        Assert.Contains("productId=7", handler.LastSearch);
        Assert.Contains("hardwareKey=key%26value", handler.LastSearch);
    }

    [Theory]
    [InlineData(404, false)]
    [InlineData(500, true)]
    public async Task RejectsMissingPartnerAndApiFailure(int status, bool unavailable)
    {
        using var handler = new ApiHandler { PartnerStatus = (HttpStatusCode)status };
        using var service = CreateService(handler);
        var error = await Assert.ThrowsAsync<LicenseValidationException>(() => service.ValidateAsync(new SapSessionContext()));
        Assert.Equal(unavailable, error.Unavailable);
        Assert.Null(handler.LastSearch);
    }

    [Fact]
    public async Task CachesTokenButChecksLicenseEveryLogin()
    {
        using var handler = new ApiHandler();
        using var service = CreateService(handler);
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => service.ValidateAsync(new SapSessionContext())));
        Assert.Equal(1, handler.Logins);
        Assert.Equal(10, handler.Searches);
    }

    [Fact]
    public async Task RetriesOnceWithNewTokenAfterUnauthorized()
    {
        using var handler = new ApiHandler { RejectTokenOnce = true };
        using var service = CreateService(handler);
        await service.ValidateAsync(new SapSessionContext());
        Assert.Equal(2, handler.Logins);
    }

    [Fact]
    public async Task PreservesCancellation()
    {
        using var handler = new ApiHandler();
        using var service = CreateService(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ValidateAsync(new SapSessionContext(), cts.Token));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"token\":{\"token\":\"\",\"expiration\":\"2099-01-01T00:00:00Z\"}}")]
    [InlineData("{\"token\":{\"token\":\"token\",\"expiration\":\"2000-01-01T00:00:00Z\"}}")]
    public async Task RejectsInvalidApiToken(string loginBody)
    {
        using var handler = new ApiHandler { LoginBody = loginBody };
        using var service = CreateService(handler);
        var error = await Assert.ThrowsAsync<LicenseValidationException>(() => service.ValidateAsync(new SapSessionContext()));
        Assert.True(error.Unavailable);
        Assert.Equal(0, handler.Searches);
    }

    [Fact]
    public async Task MissingConfigurationBlocksBeforeHttpCalls()
    {
        using var handler = new ApiHandler();
        using var service = new LicenseService(new ClientFactory(handler),
            Microsoft.Extensions.Options.Options.Create(new LicenseOptions()),
            new SapClient(), new Clock(), NullLogger<LicenseService>.Instance);
        var error = await Assert.ThrowsAsync<LicenseValidationException>(() => service.ValidateAsync(new SapSessionContext()));
        Assert.True(error.Unavailable);
        Assert.Equal(0, handler.Logins);
    }
    [Fact]
    public async Task LogsUseValidatedSessionAndIncludeTrace()
    {
        using var handler = new ApiHandler();
        using var service = CreateService(handler);
        var first = new SapSessionContext();
        await service.ValidateAsync(first);
        handler.LicenseBody = "{\"serial\":\"second-license\",\"active\":true,\"dueDate\":\"2099-01-01T00:00:00Z\"}";
        handler.PartnerBody = "{\"partnerId\":99,\"hardwareKey\":\"other-key\"}";
        var second = new SapSessionContext();
        await service.ValidateAsync(second);
        await service.LogRequestAsync(first, "Documento inserido.");
        await service.LogRequestAsync(second, "Outro documento.");
        using var log = System.Text.Json.JsonDocument.Parse(handler.Logs[0]);
        Assert.Equal(42, log.RootElement.GetProperty("partnerId").GetInt32());
        Assert.Equal("license-42", log.RootElement.GetProperty("digitalServicesLicenseLicenseSerial").GetString());
        Assert.Equal(0, log.RootElement.GetProperty("logLevel").GetInt32());
        Assert.Equal("Documento inserido.", log.RootElement.GetProperty("mensagem").GetString());
        Assert.Equal(new Clock().UtcNow, log.RootElement.GetProperty("dataLog").GetDateTime());
        using var other = System.Text.Json.JsonDocument.Parse(handler.Logs[1]);
        Assert.Equal(99, other.RootElement.GetProperty("partnerId").GetInt32());
        Assert.Equal("second-license", other.RootElement.GetProperty("digitalServicesLicenseLicenseSerial").GetString());
        Assert.Equal(1, handler.Logins);
    }

    [Fact]
    public async Task CannotLogWithoutValidatedSession()
    {
        using var handler = new ApiHandler();
        using var service = CreateService(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LogRequestAsync(new SapSessionContext(), "message"));
        Assert.Empty(handler.Logs);
        Assert.Equal(0, handler.Logins);
    }

    [Fact]
    public async Task FailedLogIsNotRetried()
    {
        using var handler = new ApiHandler { LogStatus = HttpStatusCode.InternalServerError };
        using var service = CreateService(handler);
        var session = new SapSessionContext();
        await service.ValidateAsync(session);
        await Assert.ThrowsAsync<HttpRequestException>(() => service.LogRequestAsync(session, "message"));
        Assert.Single(handler.Logs);
    }

    [Fact]
    public async Task MissingSerialCannotEstablishLicenseContext()
    {
        using var handler = new ApiHandler { LicenseBody = "{\"active\":true,\"dueDate\":\"2099-01-01T00:00:00Z\"}" };
        using var service = CreateService(handler);
        var session = new SapSessionContext();
        await Assert.ThrowsAsync<LicenseValidationException>(() => service.ValidateAsync(session));
        Assert.Null(session.License);
    }
    private static LicenseService CreateService(ApiHandler handler) => new(
        new ClientFactory(handler),
        Microsoft.Extensions.Options.Options.Create(new LicenseOptions { ProductId = "7", UserName = "test", Password = "test" }),
        new SapClient(), new Clock(), NullLogger<LicenseService>.Instance);

    private sealed class Clock : ISystemClock
    {
        public DateTime UtcNow => new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);
    }

    private sealed class ClientFactory(ApiHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false) { BaseAddress = new Uri("https://license.test/") };
    }

    private sealed class ApiHandler : HttpMessageHandler
    {
        public string LoginBody = "{\"token\":{\"token\":\"token\",\"expiration\":\"2099-01-01T00:00:00Z\"}}";
        public string LicenseBody = "{\"serial\":\"license-42\",\"active\":true,\"dueDate\":\"2099-01-01T00:00:00Z\"}";
        public string PartnerBody = "{\"partnerId\":42,\"hardwareKey\":\"key&value\"}";
        public HttpStatusCode PartnerStatus = HttpStatusCode.OK;
        public List<string> Logs { get; } = [];
        public HttpStatusCode LogStatus = HttpStatusCode.Created;
        public int Logins;
        public int Searches;
        public string? LastSearch;
        public bool RejectTokenOnce;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/LoginUser")
            {
                Interlocked.Increment(ref Logins);
                return Reply(HttpStatusCode.OK, LoginBody);
            }
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            if (RejectTokenOnce)
            {
                RejectTokenOnce = false;
                return Reply(HttpStatusCode.Unauthorized, "{}");
            }
            if (path == "/logs")
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Logs.Add(request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult());
                return Reply(LogStatus, "{}");
            }
            if (path.StartsWith("/BusinessPartners/"))
                return Reply(PartnerStatus, PartnerBody);
            Interlocked.Increment(ref Searches);
            LastSearch = request.RequestUri.Query;
            return Reply(HttpStatusCode.OK, LicenseBody);
        }
        private static Task<HttpResponseMessage> Reply(HttpStatusCode status, string body) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
    }

    private sealed class SapClient : ISapServiceLayerClient
    {
        public Task<string> GetInstallationNumberAsync(SapSessionContext session, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult("installation");
        }
        public Task<SapSessionContext> LoginAsync(string database, string userName, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task LogoutAsync(SapSessionContext session, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> PostProcessamentoAsync(SapSessionContext session, string endpoint, object payload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
