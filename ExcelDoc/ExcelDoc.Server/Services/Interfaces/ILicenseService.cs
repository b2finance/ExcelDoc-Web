using ExcelDoc.Server.Sap;

namespace ExcelDoc.Server.Services.Interfaces;

public interface ILicenseService
{
    Task LogRequestAsync(SapSessionContext session, string message, CancellationToken cancellationToken = default);

    Task ValidateAsync(SapSessionContext session, CancellationToken cancellationToken = default);
}
