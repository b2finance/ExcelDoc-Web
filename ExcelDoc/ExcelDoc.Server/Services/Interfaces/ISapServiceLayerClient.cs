using ExcelDoc.Server.Sap;

namespace ExcelDoc.Server.Services.Interfaces
{
    public interface ISapServiceLayerClient
    {
        Task<string> GetInstallationNumberAsync(
            SapSessionContext session,
            CancellationToken cancellationToken = default);

        Task<SapSessionContext> LoginAsync(
            string database,
            string userName,
            string password,
            CancellationToken cancellationToken = default);

        Task LogoutAsync(
            SapSessionContext session,
            CancellationToken cancellationToken = default);

        Task<string> PostProcessamentoAsync(
            SapSessionContext session,
            string endpoint,
            object payload,
            CancellationToken cancellationToken = default);
    }
}
