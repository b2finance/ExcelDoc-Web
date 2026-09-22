using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using B1SLayer;
using ExcelDoc.Server.Localization;
using ExcelDoc.Server.Options;
using ExcelDoc.Server.Sap;
using ExcelDoc.Server.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace ExcelDoc.Server.Services;

public sealed class SapServiceLayerClient : ISapServiceLayerClient, IDisposable
{
    private const int PortugueseLanguageCode = 29;
    private const string RequestPayloadKey = "RequestPayload";
    private const string ResponseBodyKey = "ResponseBody";
    private static readonly JsonSerializerOptions ProcessingJsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private readonly IMessageService _messageService;
    private readonly SapServiceLayerOptions _sapOptions;
    private readonly TokenBucketRateLimiter _rateLimiter;

    public SapServiceLayerClient(
        IMessageService messageService,
        IOptions<ProcessingOptions> processingOptions,
        IOptions<SapServiceLayerOptions> sapOptions)
    {
        _messageService = messageService;
        _sapOptions = sapOptions.Value;

        var permitsPerSecond = Math.Max(1, processingOptions.Value.SapRequestsPerSecond);
        _rateLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            AutoReplenishment = true,
            QueueLimit = 100,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            TokenLimit = permitsPerSecond,
            TokensPerPeriod = permitsPerSecond
        });
    }

    public async Task<SapSessionContext> LoginAsync(
        string database,
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = new SLConnection(
            NormalizeBaseAddress(_sapOptions.BaseUrl),
            database,
            userName,
            password,
            _sapOptions.Language);

        try
        {
            var login = await connection.LoginAsync();
            var timeoutMinutes = Math.Max(1, login.SessionTimeout);

            return new SapSessionContext
            {
                Connection = connection,
                ServiceLayerBaseUrl = connection.ServiceLayerRoot.ToString(),
                Database = database,
                UserName = userName,
                SessionTimeoutMinutes = timeoutMinutes,
                RequestTimeoutSeconds = _sapOptions.RequestTimeoutSeconds,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(timeoutMinutes)
            };
        }
        catch (Exception exception) when (
            SapServiceLayerErrors.IsServiceLayerException(exception))
        {
            var error = await SapServiceLayerErrors.ReadAsync(exception);
            connection.Client.Dispose();

            if (error.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
            {
                throw new UnauthorizedAccessException(
                    error.Message ?? "Usuário, senha ou base SAP inválidos.",
                    exception);
            }

            throw new InvalidOperationException(
                error.Message ?? "Não foi possível conectar ao SAP Service Layer.",
                exception);
        }
        catch
        {
            connection.Client.Dispose();
            throw;
        }
    }

    public async Task<string> GetInstallationNumberAsync(
        SapSessionContext session,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var number = await session.GetRequiredConnection()
            .Request("LicenseService_GetInstallationNumber")
            .WithTimeout(session.RequestTimeoutSeconds)
            .PostReceiveStringAsync();

        cancellationToken.ThrowIfCancellationRequested();

        return !string.IsNullOrWhiteSpace(number)
            ? number.Trim()
            : throw new InvalidOperationException("Número de instalação SAP ausente.");
    }

    public async Task LogoutAsync(
        SapSessionContext session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var connection = session.GetRequiredConnection();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await connection.LogoutAsync();
        }
        catch (Exception exception) when (
            SapServiceLayerErrors.IsServiceLayerException(exception))
        {
            await SapServiceLayerErrors.ReadAsync(exception);
        }
        finally
        {
            session.ExpiresAtUtc = DateTime.UtcNow;
            connection.Client.Dispose();
        }
    }

    public async Task<string> PostProcessamentoAsync(
        SapSessionContext session,
        string endpoint,
        object payload,
        CancellationToken cancellationToken = default)
    {
        using var lease = await _rateLimiter.AcquireAsync(1, cancellationToken);
        if (!lease.IsAcquired)
        {
            throw new InvalidOperationException(
                _messageService.Get(MessageKeys.SapRequestReservationFailed));
        }

        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        var resource = NormalizeRelativeEndpoint(endpoint);
        var requestPayload = JsonSerializer.Serialize(payload, ProcessingJsonOptions);
        try
        {
            var responseBody = await session
                .GetRequiredConnection()
                .Request(resource)
                .WithJsonSerializerOptions(ProcessingJsonOptions)
                .WithTimeout(session.RequestTimeoutSeconds)
                .PostReceiveStringAsync(payload);
            session.RenewExpiration();
            return BuildProcessamentoResponse(responseBody);
        }
        catch (Exception exception) when (
            SapServiceLayerErrors.IsServiceLayerException(exception))
        {
            var error = await SapServiceLayerErrors.ReadAsync(exception);
            SapServiceLayerErrors.UpdateSessionExpiration(session, error.StatusCode);
            var translatedException = SapServiceLayerErrors.CreateException(
                error,
                exception,
                _messageService.Get(MessageKeys.SapServiceLayerOperationFailed),
                _messageService.Get(MessageKeys.SapSessionExpired));
            translatedException.Data[RequestPayloadKey] = requestPayload;
            translatedException.Data[ResponseBodyKey] = error.ResponseBody ?? string.Empty;
            throw translatedException;
        }
    }

    private string BuildProcessamentoResponse(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                _messageService.Get(MessageKeys.SapProcessingResponseInvalid));
        }

        var documentLines = root.TryGetProperty("DocumentLines", out var lines) &&
                            lines.ValueKind == JsonValueKind.Array
            ? lines.EnumerateArray().Select(line => (object)new
            {
                ItemCode = GetJsonProperty(line, "ItemCode"),
                Quantity = GetJsonProperty(line, "Quantity"),
                Price = GetJsonProperty(line, "Price")
            }).ToArray()
            : Array.Empty<object>();

        return JsonSerializer.Serialize(new
        {
            DocEntry = GetJsonProperty(root, "DocEntry"),
            DocNum = GetJsonProperty(root, "DocNum"),
            CardCode = GetJsonProperty(root, "CardCode"),
            CardName = GetJsonProperty(root, "CardName"),
            SequenceSerial = GetJsonProperty(root, "SequenceSerial"),
            DocDate = GetJsonProperty(root, "DocDate"),
            DocumentLines = documentLines
        }, ProcessingJsonOptions);
    }

    private static JsonElement? GetJsonProperty(JsonElement source, string propertyName) =>
        source.ValueKind == JsonValueKind.Object && source.TryGetProperty(propertyName, out var value)
            ? value
            : null;

    private static string NormalizeBaseAddress(string serviceLayerUrl)
    {
        var normalized = serviceLayerUrl.Trim().TrimEnd('/');
        if (!normalized.Contains("/b1s/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = $"{normalized}/b1s/v1";
        }

        return normalized;
    }

    private static string NormalizeRelativeEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException(
                "O endpoint da Service Layer é obrigatório.",
                nameof(endpoint));
        }

        var normalized = endpoint.Trim();
        if (normalized.StartsWith('/') ||
            normalized.StartsWith('\\') ||
            Uri.TryCreate(normalized, UriKind.Absolute, out _) ||
            normalized.Split('?', 2)[0]
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment is "." or ".."))
        {
            throw new InvalidOperationException(
                "O endpoint da Service Layer deve ser um caminho relativo seguro.");
        }

        return Uri.TryCreate(normalized, UriKind.Relative, out _)
            ? normalized
            : throw new InvalidOperationException(
                "O endpoint da Service Layer é inválido.");
    }

    public void Dispose()
    {
        _rateLimiter.Dispose();
    }
}
