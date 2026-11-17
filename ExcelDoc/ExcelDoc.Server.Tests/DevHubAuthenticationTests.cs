using System.Net;
using System.Text;
using ExcelDoc.Server.Controllers;
using ExcelDoc.Server.DTOs.Auth;
using ExcelDoc.Server.Sap;
using ExcelDoc.Server.Services;
using ExcelDoc.Server.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ExcelDoc.Server.Tests;

public sealed class DevHubAuthenticationTests
{
    [Fact]
    public async Task LoginReturnsOnlyActiveAuthorizedPartnersAndUsesBearerToken()
    {
        var handler = new DevHubHandler();
        var service = CreateService(handler);

        var login = await service.LoginAsync("dev-user", "secret", default);

        Assert.Single(login.Partners);
        Assert.Equal(3, login.Partners[0].PartnerId);
        Assert.DoesNotContain("dev-token", login.SessionKey);
        Assert.All(handler.AuthorizationHeaders, value => Assert.Equal("Bearer dev-token", value));
    }

    [Fact]
    public async Task LoginRejectsExpiredDevHubToken()
    {
        var handler = new DevHubHandler { ExpiredToken = true };
        var service = CreateService(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoginAsync("dev-user", "secret", default));
        Assert.DoesNotContain("/Users/me", handler.Paths);
    }

    [Fact]
    public async Task FailedSapLoginDoesNotCreateAConnection()
    {
        var service = CreateService(new DevHubHandler());
        var login = await service.LoginAsync("dev-user", "secret", default);
        await service.SelectAsync(login.SessionKey, 3, default);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ConnectAsync(
            login.SessionKey, 3, 8, "wrong", new FakeAuth { RejectLogin = true }, new FakeAccessor(), default));
    }

    [Fact]
    public async Task SelectRejectsUnauthorizedPartnerBeforeCallingDevHub()
    {
        var handler = new DevHubHandler();
        var service = CreateService(handler);
        var login = await service.LoginAsync("dev-user", "secret", default);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SelectAsync(login.SessionKey, 99, default));

        Assert.DoesNotContain("/BusinessPartners/99/databases", handler.Paths);
        Assert.DoesNotContain("/ServiceLayerInformations/partner/99", handler.Paths);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task SelectRequiresBothRequestsToSucceed(bool failDatabases, bool failServiceLayer)
    {
        var handler = new DevHubHandler { FailDatabases = failDatabases, FailServiceLayer = failServiceLayer };
        var service = CreateService(handler);
        var login = await service.LoginAsync("dev-user", "secret", default);

        await Assert.ThrowsAsync<DevHubNotFoundException>(() => service.SelectAsync(login.SessionKey, 3, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ConnectAsync(
            login.SessionKey, 3, 8, "sap-secret", new FakeAuth(), new FakeAccessor(), default));

        Assert.Contains("/BusinessPartners/3/databases", handler.Paths);
        Assert.Contains("/ServiceLayerInformations/partner/3", handler.Paths);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task SelectRejectsDataBelongingToAnotherPartner(bool wrongDatabase, bool wrongServiceLayer)
    {
        var handler = new DevHubHandler { WrongDatabase = wrongDatabase, WrongServiceLayer = wrongServiceLayer };
        var service = CreateService(handler);
        var login = await service.LoginAsync("dev-user", "secret", default);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SelectAsync(login.SessionKey, 3, default));
    }

    [Theory]
    [InlineData("/BusinessPartners/3/databases", "Parceiro não encontrado.")]
    [InlineData("/ServiceLayerInformations/partner/3", "O parceiro não possui uma configuração de Service Layer.")]
    public async Task SelectReturnsDevHubNotFoundMessageToTheClient(string failedPath, string message)
    {
        var handler = new DevHubHandler
        {
            FailurePath = failedPath,
            FailureBody = System.Text.Json.JsonSerializer.Serialize(new { Status = 404, Message = message })
        };
        var service = CreateService(handler);
        var login = await service.LoginAsync("dev-user", "secret", default);
        var controller = new DevHubAuthController(service, new FakeAuth(), new FakeAccessor())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.Request.Headers["X-DevHub-Session"] = login.SessionKey;

        var result = await controller.Select(new DevHubSelectRequest { PartnerId = 3 }, default);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal(404, problem.Status);
        Assert.Equal(message, problem.Detail);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ConnectAsync(
            login.SessionKey, 3, 8, "sap-secret", new FakeAuth(), new FakeAccessor(), default));
    }

    [Theory]
    [InlineData("/api/LoginUser")]
    [InlineData("/Users/me")]
    [InlineData("/BusinessPartners/3")]
    public async Task LoginReturnsDevHubNotFoundMessage(string failedPath)
    {
        var handler = new DevHubHandler
        {
            FailurePath = failedPath,
            FailureBody = "{\"message\":\"Registro não encontrado.\"}"
        };
        var controller = new DevHubAuthController(CreateService(handler), new FakeAuth(), new FakeAccessor());

        var result = await controller.Login(new DevHubCredentials { UserName = "dev-user", Password = "secret" }, default);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal("Registro não encontrado.", problem.Detail);
    }

    [Theory]
    [InlineData("")]
    [InlineData("não é JSON")]
    [InlineData("{\"Message\":null}")]
    [InlineData("{\"Message\":\"  \"}")]
    public async Task SelectUsesFallbackForNotFoundWithoutValidMessage(string body)
    {
        var handler = new DevHubHandler
        {
            FailurePath = "/ServiceLayerInformations/partner/3",
            FailureBody = body
        };
        var service = CreateService(handler);
        var login = await service.LoginAsync("dev-user", "secret", default);

        var exception = await Assert.ThrowsAsync<DevHubNotFoundException>(
            () => service.SelectAsync(login.SessionKey, 3, default));

        Assert.Equal("Não foi possível consultar os dados no DevHub. Tente novamente.", exception.Message);
    }

    [Fact]
    public async Task SelectDoesNotExposeResponseBodyForOtherDevHubErrors()
    {
        var handler = new DevHubHandler
        {
            FailurePath = "/ServiceLayerInformations/partner/3",
            FailureStatus = HttpStatusCode.InternalServerError,
            FailureBody = "{\"Message\":\"Detalhe interno\"}"
        };
        var service = CreateService(handler);
        var login = await service.LoginAsync("dev-user", "secret", default);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SelectAsync(login.SessionKey, 3, default));

        Assert.Equal("Não foi possível consultar os dados no DevHub. Tente novamente.", exception.Message);
    }

    [Fact]
    public async Task ConnectUsesOnlySelectedDatabaseAndServiceLayer()
    {
        var handler = new DevHubHandler();
        var store = new SapSessionStore();
        var service = CreateService(handler, store);
        var login = await service.LoginAsync("dev-user", "secret", default);
        var selection = await service.SelectAsync(login.SessionKey, 3, default);
        var auth = new FakeAuth();
        var accessor = new FakeAccessor();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConnectAsync(
            login.SessionKey, 3, 99, "sap-secret", auth, accessor, default));
        await service.ConnectAsync(login.SessionKey, 3, 8, "sap-secret", auth, accessor, default);

        Assert.Single(selection.Databases);
        Assert.Equal("https://sap.example.test/b1s/v1", auth.Url);
        Assert.Equal("BASE3", auth.Database);
        Assert.Equal("sap-user", auth.UserName);
        Assert.Equal("dev-user", auth.DevHubUserName);
    }

    [Fact]
    public async Task SwitchingPartnerInvalidatesThePreviousSapSessionEvenWithAnActiveJob()
    {
        var store = new SapSessionStore();
        var oldSession = new SapSessionContext { SessionKey = "sap-key", ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5) };
        store.Add(oldSession);
        Assert.True(store.TryAcquireJob(oldSession.SessionKey));
        var service = CreateService(new DevHubHandler(), store);
        var login = await service.LoginAsync("dev-user", "secret", default);
        await service.SelectAsync(login.SessionKey, 3, default);
        await service.ConnectAsync(login.SessionKey, 3, 8, "sap-secret", new FakeAuth(), new FakeAccessor(), default);

        await service.SelectAsync(login.SessionKey, 3, default);

        Assert.True(oldSession.IsInvalidated);
        Assert.False(store.TryGetForJob(oldSession.SessionKey, out _));
        Assert.Same(oldSession, store.ReleaseJob(oldSession.SessionKey));
    }

    private static DevHubAuthenticationService CreateService(DevHubHandler handler, SapSessionStore? store = null)
    {
        return new DevHubAuthenticationService(new FakeClientFactory(handler), store ?? new SapSessionStore(), new FakeSapClient());
    }

    private sealed class FakeClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(handler, false) { BaseAddress = new Uri("https://devhub.example.test/") };
        }
    }

    private sealed class DevHubHandler : HttpMessageHandler
    {
        public string? FailurePath { get; init; }
        public string FailureBody { get; init; } = string.Empty;
        public HttpStatusCode FailureStatus { get; init; } = HttpStatusCode.NotFound;
        public bool ExpiredToken { get; init; }
        public bool FailDatabases { get; init; }
        public bool FailServiceLayer { get; init; }
        public bool WrongDatabase { get; init; }
        public bool WrongServiceLayer { get; init; }
        public List<string> Paths { get; } = [];
        public List<string> AuthorizationHeaders { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            lock (Paths)
            {
                Paths.Add(path);
                if (path != "/api/LoginUser")
                {
                    AuthorizationHeaders.Add(request.Headers.Authorization?.ToString() ?? "");
                }
            }

            if (path == FailurePath)
            {
                return Task.FromResult(new HttpResponseMessage(FailureStatus)
                {
                    Content = new StringContent(FailureBody, Encoding.UTF8, "application/json")
                });
            }

            if ((path == "/BusinessPartners/3/databases" && FailDatabases) ||
                (path == "/ServiceLayerInformations/partner/3" && FailServiceLayer))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            var body = path switch
            {
                "/api/LoginUser" => $"{{\"token\":{{\"token\":\"dev-token\",\"expiration\":\"{(ExpiredToken ? "2000" : "2099")}-01-01T00:00:00Z\"}}}}",
                "/Users/me" => "{\"username\":\"dev-user\",\"isActive\":true,\"businessPartnerIds\":[3,4]}",
                "/BusinessPartners/3" => "{\"partnerId\":3,\"partnerName\":\"Ativo\",\"active\":true}",
                "/BusinessPartners/4" => "{\"partnerId\":4,\"partnerName\":\"Inativo\",\"active\":false}",
                "/BusinessPartners/3/databases" => $"[{{\"id\":8,\"nomeBase\":\"BASE3\",\"partnerId\":{(WrongDatabase ? 4 : 3)}}}]",
                "/ServiceLayerInformations/partner/3" => $"{{\"url\":\"https://sap.example.test/b1s/v1\",\"userName\":\"sap-user\",\"partnerId\":{(WrongServiceLayer ? 4 : 3)},\"partnerName\":\"Ativo\"}}",
                _ => throw new InvalidOperationException("Unexpected endpoint")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FakeAuth : IAuthService
    {
        public bool RejectLogin { get; init; }
        public string? Url { get; private set; }
        public string? Database { get; private set; }
        public string? UserName { get; private set; }
        public string? DevHubUserName { get; private set; }
        public IReadOnlyCollection<SapBaseDto> GetBases() => [];
        public Task<LoginResponseDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<LoginResponseDto> LoginSelectedAsync(string serviceLayerUrl, string database, string sapUserName,
            string password, string devHubUserName, CancellationToken cancellationToken = default)
        {
            if (RejectLogin)
            {
                throw new UnauthorizedAccessException("Credenciais SAP inválidas.");
            }

            Url = serviceLayerUrl;
            Database = database;
            UserName = sapUserName;
            DevHubUserName = devHubUserName;
            return Task.FromResult(new LoginResponseDto());
        }
    }

    private sealed class FakeAccessor : ISapSessionContextAccessor
    {
        public SapSessionContext GetRequiredSession() => throw new NotSupportedException();
        public string GetRequiredSessionKey() => "sap-key";
        public void SetSessionKey(string sessionKey) { }
        public void SetJobSessionKey(string sessionKey) { }
    }

    private sealed class FakeSapClient : ISapServiceLayerClient
    {
        public Task<SapSessionContext> LoginAsync(string database, string userName, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task LogoutAsync(SapSessionContext session, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> IsSuperuserAsync(SapSessionContext session, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> GetInstallationNumberAsync(SapSessionContext session, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> PostProcessamentoAsync(SapSessionContext session, string endpoint, object payload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, int>> GetNFModelsAsync(SapSessionContext session, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
