namespace ExcelDoc.Server.Services;

public sealed class DevHubLicenseCredentials(string userName, string password)
{
    internal string UserName { get; } = userName;
    internal string Password { get; } = password;
    internal SemaphoreSlim TokenLock { get; } = new(1, 1);
    internal string? Token { get; set; }
    internal DateTimeOffset TokenExpiration { get; set; }
}
