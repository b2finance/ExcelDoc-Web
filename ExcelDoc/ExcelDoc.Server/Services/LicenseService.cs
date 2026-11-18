using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ExcelDoc.Server.Options;
using ExcelDoc.Server.Sap;
using ExcelDoc.Server.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace ExcelDoc.Server.Services;

public sealed class LicenseService(
    IHttpClientFactory httpClientFactory,
    IOptions<LicenseOptions> options,
    ISapServiceLayerClient sapClient,
    ISystemClock clock,
    ILogger<LicenseService> logger) : ILicenseService
{
    public async Task ValidateAsync(SapSessionContext session, DevHubLicenseCredentials credentials, CancellationToken cancellationToken = default)
    {
        session.License = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var config = options.Value;

            if (string.IsNullOrWhiteSpace(config.ProductId) ||
                string.IsNullOrWhiteSpace(credentials.UserName) ||
                string.IsNullOrWhiteSpace(credentials.Password))
            {
                throw new InvalidOperationException("Configuração de licenciamento incompleta.");
            }

            var installation = await sapClient.GetInstallationNumberAsync(session, cancellationToken);
            if (string.IsNullOrWhiteSpace(installation))
                throw new InvalidOperationException("Número de instalação SAP ausente.");

            using var client = httpClientFactory.CreateClient(DevHubAuthenticationService.ClientName);
            var partner = await GetAsync<BusinessPartner>(client, credentials,
                $"BusinessPartners/installation-number/{Uri.EscapeDataString(installation)}", cancellationToken);

            if (partner is null)
                throw NotFound();

            if (partner.PartnerId <= 0 || string.IsNullOrWhiteSpace(partner.HardwareKey))
                throw new JsonException("Parceiro inválido.");

            var license = await GetAsync<ProductLicense>(client, credentials,
                $"licenses/search?partnerId={partner.PartnerId}&hardwareKey={Uri.EscapeDataString(partner.HardwareKey)}&productId={Uri.EscapeDataString(config.ProductId)}",
                cancellationToken);

            if (license is null)
                throw NotFound();

            if (!license.Active || license.DueDate < new DateTimeOffset(clock.UtcNow))
                throw new LicenseValidationException("A licença não está ativa ou está expirada. Entre em contato com o suporte.");

            if (string.IsNullOrWhiteSpace(license.Serial))
                throw new JsonException("Serial de licença ausente.");
            
            session.License = new SapLicenseContext(partner.PartnerId, license.Serial, credentials);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (LicenseValidationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning("Falha na consulta de licença ({ExceptionType}).", exception.GetType().Name);
            throw new LicenseValidationException(
                "Não foi possível validar a licença no momento. Tente novamente ou entre em contato com o suporte.", true);
        }
    }

    public async Task LogRequestAsync(
        SapSessionContext session,
        string message,
        CancellationToken cancellationToken = default)
    {
        var license = session.License;
        if (license is null || license.PartnerId <= 0 || string.IsNullOrWhiteSpace(license.LicenseSerial))
            throw new InvalidOperationException("Valide a licença da sessão antes de enviar logs.");

        using var client = httpClientFactory.CreateClient(DevHubAuthenticationService.ClientName);
        var token = await GetTokenAsync(client, license.Credentials, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, "logs");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        
        request.Content = JsonContent.Create(new
        {
            Mensagem = message,
            DataLog = clock.UtcNow,
            LogLevel = LogLevel.Trace,
            DigitalServicesLicenseLicenseSerial = license.LicenseSerial,
            license.PartnerId
        });
        
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static LicenseValidationException NotFound() =>
        new("Licença não encontrada para este cliente. Entre em contato com o suporte.");

    private async Task<T?> GetAsync<T>(HttpClient client, DevHubLicenseCredentials credentials, string endpoint, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var token = await GetTokenAsync(client, credentials, ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                await credentials.TokenLock.WaitAsync(ct);
                try
                {
                    if (credentials.Token == token)
                        credentials.Token = null;
                }
                finally
                {
                    credentials.TokenLock.Release();
                }

                continue;
            }

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent)
                return default;

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>(ct);
        }
        throw new HttpRequestException("Autenticação da API de licenças recusada.");
    }

    private async Task<string> GetTokenAsync(HttpClient client, DevHubLicenseCredentials credentials, CancellationToken ct)
    {
        await credentials.TokenLock.WaitAsync(ct);
        try
        {
            if (!string.IsNullOrWhiteSpace(credentials.Token) &&
                new DateTimeOffset(clock.UtcNow) < credentials.TokenExpiration.AddSeconds(-30))
                return credentials.Token;

            using var response = await client.PostAsJsonAsync("api/LoginUser", new
            {
                credentials.UserName,
                credentials.Password
            }, ct);

            response.EnsureSuccessStatusCode();
            var login = await response.Content.ReadFromJsonAsync<LoginResponse>(ct);

            if (string.IsNullOrWhiteSpace(login?.Token?.Token) ||
                login.Token.Expiration <= new DateTimeOffset(clock.UtcNow))
                throw new JsonException("Token de licenciamento inválido.");

            credentials.Token = login.Token.Token;
            credentials.TokenExpiration = login.Token.Expiration;

            return credentials.Token;
        }
        finally { credentials.TokenLock.Release(); }
    }

    private sealed record BusinessPartner(int PartnerId, string HardwareKey);
    private sealed record ProductLicense(bool Active, DateTimeOffset DueDate, string? Serial);
    private sealed record LoginResponse(ApiToken? Token);
    private sealed record ApiToken(string Token, DateTimeOffset Expiration);
}
