using ExcelDoc.Server.Services;

namespace ExcelDoc.Server.Sap;

public sealed class SapLicenseContext(int partnerId, string licenseSerial, DevHubLicenseCredentials credentials)
{
    public int PartnerId { get; } = partnerId;
    public string LicenseSerial { get; } = licenseSerial;
    internal DevHubLicenseCredentials Credentials { get; } = credentials;
}
