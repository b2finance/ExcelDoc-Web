using ExcelDoc.Server.DTOs.Auth;

namespace ExcelDoc.Server.Services.Interfaces
{
    public interface IAuthService
    {
        IReadOnlyCollection<SapBaseDto> GetBases();

        Task<LoginResponseDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default);

        Task<LoginResponseDto> LoginSelectedAsync(
            string serviceLayerUrl,
            string database,
            string sapUserName,
            string password,
            string devHubUserName,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        Task LogoutAsync(CancellationToken cancellationToken = default);
    }
}
