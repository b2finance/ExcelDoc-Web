using B1SLayer;

namespace ExcelDoc.Server.Sap;

public sealed class SapSessionContext
{
    private int _invalidated;

    public bool IsInvalidated => Volatile.Read(ref _invalidated) != 0;

    public void Invalidate()
    {
        Interlocked.Exchange(ref _invalidated, 1);
        Connection?.Client.HttpClient.CancelPendingRequests();
    }

    public string SessionKey { get; init; } = Guid.NewGuid().ToString("N");

    public string ServiceLayerBaseUrl { get; init; } = string.Empty;

    public string Database { get; init; } = string.Empty;

    public string UserName { get; init; } = string.Empty;

    public SapLicenseContext? License { get; internal set; }

    public SLConnection? Connection { get; init; }

    public int SessionTimeoutMinutes { get; init; } = 30;

    public int RequestTimeoutSeconds { get; init; } = 100;

    public DateTime ExpiresAtUtc { get; set; }

    public void RenewExpiration() =>
        ExpiresAtUtc = DateTime.UtcNow.AddMinutes(SessionTimeoutMinutes);

    public SLConnection GetRequiredConnection()
    {
        if (IsInvalidated)
        {
            throw new SapSessionExpiredException("O contexto SAP foi encerrado.");
        }

        return Connection ?? throw new InvalidOperationException(
            "A conexão B1SLayer não está disponível para esta sessão SAP.");
    }
}
