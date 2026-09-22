using ExcelDoc.Server.Sap;

namespace ExcelDoc.Server.Services.Interfaces;

public interface ILicenseService
{
    Task ValidateAsync(SapSessionContext session, CancellationToken cancellationToken = default);
}
