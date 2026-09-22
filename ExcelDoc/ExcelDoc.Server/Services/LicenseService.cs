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
    ILogger<LicenseService> logger) : ILicenseService, IDisposable
{
    public const string ClientName = "DevHub";
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenExpiration;

    public async Task ValidateAsync(SapSessionContext session, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var config = options.Value;
            config.UserName = "B2Finance";
            config.Password = "B1@Admin";

            if (string.IsNullOrWhiteSpace(config.ProductId) ||
                string.IsNullOrWhiteSpace(config.UserName) ||
                string.IsNullOrWhiteSpace(config.Password))
                throw new InvalidOperationException("Configuração de licenciamento incompleta.");

            var installation = await sapClient.GetInstallationNumberAsync(session, cancellationToken);
            if (string.IsNullOrWhiteSpace(installation))
                throw new InvalidOperationException("Número de instalação SAP ausente.");

            using var client = httpClientFactory.CreateClient(ClientName);
            var partner = await GetAsync<BusinessPartner>(client,
                $"BusinessPartners/installation-number/{Uri.EscapeDataString(installation)}", cancellationToken);

            if (partner is null)
                throw NotFound();

            if (partner.PartnerId <= 0 || string.IsNullOrWhiteSpace(partner.HardwareKey))
                throw new JsonException("Parceiro inválido.");

            var license = await GetAsync<ProductLicense>(client,
                $"licenses/search?partnerId={partner.PartnerId}&hardwareKey={Uri.EscapeDataString(partner.HardwareKey)}&productId={Uri.EscapeDataString(config.ProductId)}",
                cancellationToken);

            if (license is null)
                throw NotFound();

            if (!license.Active || license.DueDate < new DateTimeOffset(clock.UtcNow))
                throw new LicenseValidationException("A licença não está ativa ou está expirada. Entre em contato com o suporte.");
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

    private static LicenseValidationException NotFound() =>
        new("Licença não encontrada para este cliente. Entre em contato com o suporte.");

    private async Task<T?> GetAsync<T>(HttpClient client, string endpoint, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var token = await GetTokenAsync(client, ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                await _tokenLock.WaitAsync(ct);
                try
                {
                    if (_token == token)
                        _token = null;
                }
                finally
                {
                    _tokenLock.Release();
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

    private async Task<string> GetTokenAsync(HttpClient client, CancellationToken ct)
    {
        await _tokenLock.WaitAsync(ct);
        try
        {
            if (!string.IsNullOrWhiteSpace(_token) &&
                new DateTimeOffset(clock.UtcNow) < _tokenExpiration.AddSeconds(-30))
                return _token;

            using var response = await client.PostAsJsonAsync("api/LoginUser", new
            {
                options.Value.UserName,
                options.Value.Password
            }, ct);

            response.EnsureSuccessStatusCode();
            var login = await response.Content.ReadFromJsonAsync<LoginResponse>(ct);

            if (string.IsNullOrWhiteSpace(login?.Token?.Token) ||
                login.Token.Expiration <= new DateTimeOffset(clock.UtcNow))
                throw new JsonException("Token de licenciamento inválido.");

            _token = login.Token.Token;
            _tokenExpiration = login.Token.Expiration;

            return _token;
        }
        finally { _tokenLock.Release(); }
    }

    private sealed record BusinessPartner(int PartnerId, string HardwareKey);
    private sealed record ProductLicense(bool Active, DateTimeOffset DueDate);
    private sealed record LoginResponse(ApiToken? Token);
    private sealed record ApiToken(string Token, DateTimeOffset Expiration);

    public void Dispose() => _tokenLock.Dispose();
}
