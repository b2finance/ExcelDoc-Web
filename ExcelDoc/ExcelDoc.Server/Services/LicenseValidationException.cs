namespace ExcelDoc.Server.Services;

public sealed class LicenseValidationException(string message, bool unavailable = false)
    : Exception(message)
{
    public bool Unavailable { get; } = unavailable;
}
