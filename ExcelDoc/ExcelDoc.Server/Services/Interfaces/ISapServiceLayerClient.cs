using ExcelDoc.Server.Sap;

namespace ExcelDoc.Server.Services.Interfaces
{
    public interface ISapServiceLayerClient
    {
        Task<bool> IsSuperuserAsync(SapSessionContext session, CancellationToken cancellationToken = default);

        Task<string> GetInstallationNumberAsync(
            SapSessionContext session,
            CancellationToken cancellationToken = default);

        Task<SapSessionContext> LoginAsync(
            string serviceLayerUrl,
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

        Task<IReadOnlyDictionary<string, int>> GetNFModelsAsync(
            SapSessionContext session,
            CancellationToken cancellationToken = default);
    }
}
