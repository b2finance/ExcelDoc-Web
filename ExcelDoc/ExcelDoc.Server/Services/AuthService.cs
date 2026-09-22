using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ExcelDoc.Server.DTOs.Auth;
using ExcelDoc.Server.Models;
using ExcelDoc.Server.Options;
using ExcelDoc.Server.Sap;
using ExcelDoc.Server.Security;
using ExcelDoc.Server.Services.Interfaces;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ExcelDoc.Server.Services;

public sealed class AuthService : IAuthService
{
    private static readonly HashSet<string> AdministratorUsers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "manager",
            "Support"
        };

    private readonly ISapDatabaseInitializer _databaseInitializer;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<AuthService> _logger;
    private readonly SapServiceLayerOptions _sapOptions;
    private readonly ISapServiceLayerClient _sapServiceLayerClient;
    private readonly ISapSessionContextAccessor _sessionAccessor;
    private readonly ISapSessionStore _sessionStore;
    private readonly ISystemClock _systemClock;
    private readonly ILicenseService _licenseService;

    public AuthService(
        ISapDatabaseInitializer databaseInitializer,
        IOptions<JwtOptions> jwtOptions,
        ILogger<AuthService> logger,
        IOptions<SapServiceLayerOptions> sapOptions,
        ISapServiceLayerClient sapServiceLayerClient,
        ISapSessionContextAccessor sessionAccessor,
        ISapSessionStore sessionStore,
        ISystemClock systemClock,
        ILicenseService licenseService)
    {
        _databaseInitializer = databaseInitializer;
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
        _sapOptions = sapOptions.Value;
        _sapServiceLayerClient = sapServiceLayerClient;
        _sessionAccessor = sessionAccessor;
        _sessionStore = sessionStore;
        _systemClock = systemClock;
        _licenseService = licenseService;
    }

    public IReadOnlyCollection<SapBaseDto> GetBases()
    {
        return _sapOptions.Bases
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.Database) &&
                !string.IsNullOrWhiteSpace(item.Description))
            .GroupBy(item => item.Database.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Description, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => new SapBaseDto
            {
                Database = item.Database.Trim(),
                Description = item.Description.Trim()
            })
            .ToList();
    }

    public async Task<LoginResponseDto> LoginAsync(
        LoginRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var configuredBase = _sapOptions.Bases.FirstOrDefault(item =>
            string.Equals(
                item.Database.Trim(),
                request.Database.Trim(),
                StringComparison.OrdinalIgnoreCase));

        if (configuredBase is null)
        {
            throw new InvalidOperationException(
                "A base SAP Business One selecionada não está configurada para esta instalação.");
        }

        var database = configuredBase.Database.Trim();
        var userName = request.Login.Trim();
        var isAdministrator = AdministratorUsers.Contains(userName);
        var session = await _sapServiceLayerClient.LoginAsync(
            database,
            userName,
            request.Senha,
            cancellationToken);

        try
        {
            await _licenseService.ValidateAsync(session, cancellationToken);
            _sessionStore.Add(session);
            _sessionAccessor.SetSessionKey(session.SessionKey);

            await _databaseInitializer.InitializeAsync(
                session,
                isAdministrator,
                cancellationToken);
        }
        catch
        {
            _sessionStore.Remove(session.SessionKey, out _);
            try
            {
                await _sapServiceLayerClient.LogoutAsync(session, CancellationToken.None);
            }
            catch (Exception logoutException)
            {
                _logger.LogDebug(
                    logoutException,
                    "Falha ao encerrar sessão SAP após login recusado.");
            }

            throw;
        }

        var role = isAdministrator
            ? TipoUsuario.Administrador.ToString()
            : TipoUsuario.Usuario.ToString();
        var expiresAt = _systemClock.UtcNow.AddMinutes(
            Math.Max(1, _jwtOptions.ExpirationMinutes));
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userName),
            new(ClaimTypes.Name, userName),
            new(ClaimTypes.GivenName, userName),
            new(ClaimTypes.Role, role),
            new(CustomClaimTypes.Database, database),
            new(CustomClaimTypes.SapSessionKey, session.SessionKey)
        };

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_jwtOptions.SecretKey));
        var credentials = new SigningCredentials(
            key,
            SecurityAlgorithms.HmacSha256);
        var tokenDescriptor = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);
        var token = new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);

        _logger.LogInformation(
            "Usuário SAP {UserName} autenticado na base {Database} com perfil {Role}.",
            userName,
            database,
            role);

        return new LoginResponseDto
        {
            Token = token,
            ExpiresAtUtc = expiresAt,
            NomeUsuario = userName,
            TipoUsuario = role,
            Database = database,
            Idioma = "pt"
        };
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var sessionKey = _sessionAccessor.GetRequiredSessionKey();
        var session = _sessionStore.RequestLogout(sessionKey);
        if (session is null)
        {
            return;
        }

        await _sapServiceLayerClient.LogoutAsync(session, cancellationToken);
    }
}
