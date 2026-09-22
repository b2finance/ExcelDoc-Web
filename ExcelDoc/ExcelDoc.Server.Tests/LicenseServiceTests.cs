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
    [InlineData("{\"active\":true,\"dueDate\":\"2099-01-01T00:00:00Z\"}", true)]
    [InlineData("{\"active\":false,\"dueDate\":\"2099-01-01T00:00:00Z\"}", false)]
    [InlineData("{\"active\":true,\"dueDate\":\"2000-01-01T00:00:00Z\"}", false)]
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
        public string LicenseBody = "{\"active\":true,\"dueDate\":\"2099-01-01T00:00:00Z\"}";
        public HttpStatusCode PartnerStatus = HttpStatusCode.OK;
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
            if (path.StartsWith("/BusinessPartners/"))
                return Reply(PartnerStatus, "{\"partnerId\":42,\"hardwareKey\":\"key&value\"}");
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
