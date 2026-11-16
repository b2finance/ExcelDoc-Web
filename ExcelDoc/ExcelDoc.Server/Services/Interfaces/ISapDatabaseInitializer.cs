using ExcelDoc.Server.Sap;

namespace ExcelDoc.Server.Services.Interfaces;

public interface ISapDatabaseInitializer
{
    Task InitializeAsync(
        SapSessionContext session,
        CancellationToken cancellationToken = default);
}
