using System.ComponentModel.DataAnnotations;
using ExcelDoc.Server.Services;
using ExcelDoc.Server.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExcelDoc.Server.Controllers;

[ApiController]
[Route("api/devhub-auth")]
[AllowAnonymous]
public sealed class DevHubAuthController(
    DevHubAuthenticationService service,
    IAuthService authService,
    ISapSessionContextAccessor accessor) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login(DevHubCredentials request, CancellationToken cancellationToken)
    {
        return await ExecuteAsync(() => service.LoginAsync(request.UserName.Trim(), request.Password, cancellationToken));
    }

    [HttpPost("select")]
    public async Task<IActionResult> Select(DevHubSelectRequest request, CancellationToken cancellationToken)
    {
        return await ExecuteAsync(() => service.SelectAsync(SessionKey, request.PartnerId, cancellationToken));
    }

    [HttpPost("connect")]
    public async Task<IActionResult> Connect(DevHubConnectRequest request, CancellationToken cancellationToken)
    {
        return await ExecuteAsync(() => service.ConnectAsync(
            SessionKey, request.PartnerId, request.DatabaseId, request.SapUserName, request.Password,
            authService, accessor, cancellationToken));
    }

    [HttpPost("disconnect")]
    public async Task<IActionResult> Disconnect()
    {
        try
        {
            await service.DisconnectAsync(SessionKey);
            return NoContent();
        }
        catch (UnauthorizedAccessException exception)
        {
            return Unauthorized(new ProblemDetails { Status = 401, Detail = exception.Message });
        }
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await service.LogoutAsync(SessionKey);
        return NoContent();
    }

    private string SessionKey => Request.Headers["X-DevHub-Session"].ToString();

    private async Task<IActionResult> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (UnauthorizedAccessException exception)
        {
            return Unauthorized(new ProblemDetails { Status = 401, Detail = exception.Message });
        }
        catch (LicenseValidationException exception)
        {
            var status = exception.Unavailable ? 503 : 403;
            return StatusCode(status, new ProblemDetails { Status = status, Detail = exception.Message });
        }
        catch (DevHubNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Detail = exception.Message
            });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new ProblemDetails { Status = 400, Detail = exception.Message });
        }
        catch (HttpRequestException)
        {
            return StatusCode(503, new ProblemDetails { Status = 503, Detail = "Não foi possível acessar o DevHub." });
        }
    }
}

public sealed class DevHubCredentials
{
    [Required]
    [MaxLength(100)]
    public string UserName { get; set; } = string.Empty;

    [Required]
    [MaxLength(254)]
    public string Password { get; set; } = string.Empty;
}

public sealed class DevHubSelectRequest
{
    public int PartnerId { get; set; }
}

public sealed class DevHubConnectRequest
{
    public int PartnerId { get; set; }
    public int DatabaseId { get; set; }

    [Required]
    public string SapUserName { get; set; } = string.Empty;

    [Required]
    [MaxLength(254)]
    public string Password { get; set; } = string.Empty;
}
