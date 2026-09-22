using ExcelDoc.Server.Controllers;
using ExcelDoc.Server.DTOs.Auth;
using ExcelDoc.Server.Services;
using ExcelDoc.Server.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ExcelDoc.Server.Tests;

public sealed class LicenseLoginResponseTests
{
    [Theory]
    [InlineData(false, 403)]
    [InlineData(true, 503)]
    public async Task LoginReturnsLicenseMessageForDisplay(bool unavailable, int status)
    {
        var controller = new AuthController(new DeniedAuthService(unavailable));
        var result = Assert.IsType<ObjectResult>(await controller.Login(new LoginRequestDto(), default));
        Assert.Equal(status, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal(status, problem.Status);
        Assert.Equal("Licença indisponível.", problem.Detail);
    }

    private sealed class DeniedAuthService(bool unavailable) : IAuthService
    {
        public IReadOnlyCollection<SapBaseDto> GetBases() => [];
        public Task<LoginResponseDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default) =>
            Task.FromException<LoginResponseDto>(new LicenseValidationException("Licença indisponível.", unavailable));
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
