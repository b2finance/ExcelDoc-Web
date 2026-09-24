using ExcelDoc.Server.DTOs.Auth;
using ExcelDoc.Server.Models;
using ExcelDoc.Server.Options;
using ExcelDoc.Server.Sap;
using ExcelDoc.Server.Services;
using ExcelDoc.Server.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExcelDoc.Server.Tests;

public sealed class AuthServiceAuthorizationTests
{
    [Theory]
    [InlineData("manager", true)]
    [InlineData("MANAGER", true)]
    [InlineData("Support", true)]
    [InlineData("support", true)]
    [InlineData("operador", false)]
    public async Task LoginAsync_AssignsExpectedRoleAndSchemaPermission(
        string userName,
        bool expectedAdministrator)
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.LoginAsync(new LoginRequestDto
        {
            Database = " SBODEMO_BR ",
            Login = $" {userName} ",
            Senha = "secret"
        });

        Assert.Equal(
            expectedAdministrator
                ? TipoUsuario.Administrador.ToString()
                : TipoUsuario.Usuario.ToString(),
            result.TipoUsuario);
        Assert.Equal(expectedAdministrator, fixture.Initializer.AllowSchemaCreation);
        Assert.Equal("SBODEMO_BR", fixture.Client.Database);
        Assert.Equal(userName, fixture.Client.UserName);
        Assert.Equal(fixture.Client.Session.SessionKey, fixture.Accessor.SessionKey);
    }

    [Fact]
    public async Task LoginAsync_RejectsDatabaseOutsideConfiguredAllowList()
    {
        var fixture = CreateFixture();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.LoginAsync(new LoginRequestDto
            {
                Database = "NOT_CONFIGURED",
                Login = "manager",
                Senha = "secret"
            }));

        Assert.Contains("configurada", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fixture.Client.LoginCalls);
        Assert.Null(fixture.Initializer.AllowSchemaCreation);
    }

    [Theory]
    [InlineData("manager")]
    [InlineData("operador")]
    public async Task LoginAsync_RejectsLicenseBeforeInitializationAndClosesSap(string userName)
    {
        var fixture = CreateFixture(rejectLicense: true);
        await Assert.ThrowsAsync<LicenseValidationException>(() => fixture.Service.LoginAsync(new LoginRequestDto
        {
            Database = "SBODEMO_BR", Login = userName, Senha = "secret"
        }));
        Assert.Null(fixture.Initializer.AllowSchemaCreation);
        Assert.Null(fixture.Accessor.SessionKey);
        Assert.False(fixture.Store.TryGet(fixture.Client.Session.SessionKey, out _));
        Assert.Equal(1, fixture.Client.LogoutCalls);
    }

    private sealed class TestLicenseService(bool reject) : ILicenseService
    {
        public Task LogRequestAsync(SapSessionContext session, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ValidateAsync(SapSessionContext session, CancellationToken cancellationToken = default) =>
            reject ? Task.FromException(new LicenseValidationException("Licença não encontrada.")) : Task.CompletedTask;
    }
    private static TestFixture CreateFixture(bool rejectLicense = false)
    {
        var initializer = new RecordingDatabaseInitializer();
        var client = new RecordingSapServiceLayerClient();
        var accessor = new RecordingSessionAccessor();
        var store = new SapSessionStore();
        var clock = new FixedSystemClock(new DateTime(2026, 7, 23, 12, 0, 0, DateTimeKind.Utc));
        var jwtOptions = Microsoft.Extensions.Options.Options.Create(new JwtOptions
        {
            Issuer = "ExcelDoc.Tests",
            Audience = "ExcelDoc.Tests",
            SecretKey = "test-only-secret-key-with-at-least-thirty-two-characters",
            ExpirationMinutes = 60
        });
        var sapOptions = Microsoft.Extensions.Options.Options.Create(new SapServiceLayerOptions
        {
            BaseUrl = "https://sap.example.local:50000/b1s/v1",
            RequestTimeoutSeconds = 30,
            Bases =
            [
                new SapBaseOptions
                {
                    Database = "SBODEMO_BR",
                    Description = "Base de demonstração"
                }
            ]
        });

        var service = new AuthService(
            initializer,
            jwtOptions,
            NullLogger<AuthService>.Instance,
            sapOptions,
            client,
            accessor,
            store,
            clock, new TestLicenseService(rejectLicense));

        return new TestFixture(service, initializer, client, accessor, store);
    }

    private sealed record TestFixture(
        AuthService Service,
        RecordingDatabaseInitializer Initializer,
        RecordingSapServiceLayerClient Client,
        RecordingSessionAccessor Accessor, SapSessionStore Store);

    private sealed class RecordingDatabaseInitializer : ISapDatabaseInitializer
    {
        public bool? AllowSchemaCreation { get; private set; }

        public Task InitializeAsync(
            SapSessionContext session,
            bool allowSchemaCreation,
            CancellationToken cancellationToken = default)
        {
            AllowSchemaCreation = allowSchemaCreation;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSapServiceLayerClient : ISapServiceLayerClient
    {
        public Task<IReadOnlyDictionary<string, int>> GetNFModelsAsync(SapSessionContext session, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public int LogoutCalls { get; private set; }

        public Task<string> GetInstallationNumberAsync(SapSessionContext session, CancellationToken cancellationToken = default) => Task.FromResult("installation");

        public int LoginCalls { get; private set; }

        public string? Database { get; private set; }

        public string? UserName { get; private set; }

        public SapSessionContext Session { get; } = new()
        {
            ServiceLayerBaseUrl = "https://sap.example.local:50000/b1s/v1",
            Database = "SBODEMO_BR",
            UserName = "test",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        };

        public Task<SapSessionContext> LoginAsync(
            string database,
            string userName,
            string password,
            CancellationToken cancellationToken = default)
        {
            LoginCalls++;
            Database = database;
            UserName = userName;
            return Task.FromResult(Session);
        }

        public Task LogoutAsync(
            SapSessionContext session,
            CancellationToken cancellationToken = default)
        {
            LogoutCalls++;
            return Task.CompletedTask;
        }

        public Task<string> PostProcessamentoAsync(
            SapSessionContext session,
            string endpoint,
            object payload,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingSessionAccessor : ISapSessionContextAccessor
    {
        public string? SessionKey { get; private set; }

        public SapSessionContext GetRequiredSession()
        {
            throw new NotSupportedException();
        }

        public string GetRequiredSessionKey()
        {
            return SessionKey ?? throw new InvalidOperationException();
        }

        public void SetSessionKey(string sessionKey)
        {
            SessionKey = sessionKey;
        }

        public void SetJobSessionKey(string sessionKey)
        {
            SessionKey = sessionKey;
        }
    }

    private sealed class FixedSystemClock(DateTime utcNow) : ISystemClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }
}
