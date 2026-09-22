using ExcelDoc.Server.DTOs.Auth;
using ExcelDoc.Server.Services;
using ExcelDoc.Server.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExcelDoc.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [AllowAnonymous]
    [HttpGet("bases")]
    public ActionResult<IReadOnlyCollection<SapBaseDto>> GetBases()
    {
        return Ok(_authService.GetBases());
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _authService.LoginAsync(request, cancellationToken));
        }
        catch (Exception exception)
        {
            return ToActionResult(exception);
        }
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        try
        {
            await _authService.LogoutAsync(cancellationToken);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return NoContent();
        }
    }

    private ObjectResult ToActionResult(Exception exception)
    {
        return exception switch
        {
            LicenseValidationException license => StatusCode(
                license.Unavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status403Forbidden,
                new ProblemDetails
                {
                    Detail = license.Message,
                    Status = license.Unavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status403Forbidden
                }),
            UnauthorizedAccessException => Unauthorized(
                new ProblemDetails
                {
                    Detail = exception.Message,
                    Status = StatusCodes.Status401Unauthorized
                }),
            InvalidOperationException => BadRequest(
                new ProblemDetails
                {
                    Detail = exception.Message,
                    Status = StatusCodes.Status400BadRequest
                }),
            _ => StatusCode(
                StatusCodes.Status500InternalServerError,
                new ProblemDetails
                {
                    Detail = exception.Message,
                    Status = StatusCodes.Status500InternalServerError
                })
        };
    }
}
