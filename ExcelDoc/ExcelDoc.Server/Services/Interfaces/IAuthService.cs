using ExcelDoc.Server.DTOs.Auth;

namespace ExcelDoc.Server.Services.Interfaces
{
    public interface IAuthService
    {
        Task<LoginResponseDto> LoginSelectedAsync(
            string serviceLayerUrl,
            string database,
            string sapUserName,
            string password,
            string devHubUserName,
            DevHubLicenseCredentials credentials,
            CancellationToken cancellationToken = default);

        Task LogoutAsync(CancellationToken cancellationToken = default);
    }
}
