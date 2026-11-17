using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ExcelDoc.Server.DTOs.Auth;
using ExcelDoc.Server.Sap;
using ExcelDoc.Server.Services.Interfaces;

namespace ExcelDoc.Server.Services;

public sealed class DevHubAuthenticationService
{
    public const string ClientName = "DevHubAuthentication";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, DevHubSession> _sessions = new();
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISapSessionStore _sapSessions;
    private readonly ISapServiceLayerClient _sapClient;

    public DevHubAuthenticationService(
        IHttpClientFactory httpClientFactory,
        ISapSessionStore sapSessions,
        ISapServiceLayerClient sapClient)
    {
        _httpClientFactory = httpClientFactory;
        _sapSessions = sapSessions;
        _sapClient = sapClient;
    }

    public async Task<DevHubLoginResult> LoginAsync(string userName, string password, CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient(ClientName);
        using var response = await client.PostAsJsonAsync("api/LoginUser", new { userName, password }, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest)
        {
            throw new UnauthorizedAccessException("Credenciais DevHub inválidas.");
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var login = await response.Content.ReadFromJsonAsync<DevHubLogin>(JsonOptions, cancellationToken);
        if (string.IsNullOrWhiteSpace(login?.Token?.Token) || login.Token.Expiration <= DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException("O DevHub retornou um token inválido ou expirado.");
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token.Token);
        var profile = await GetAsync<DevHubProfile>(client, "Users/me", cancellationToken);
        if (!profile.IsActive || string.IsNullOrWhiteSpace(profile.Username) || profile.BusinessPartnerIds is null)
        {
            throw new UnauthorizedAccessException("Usuário DevHub inativo ou sem clientes autorizados.");
        }

        var partners = new List<DevHubPartner>();
        foreach (var id in profile.BusinessPartnerIds.Distinct())
        {
            var partner = await GetAsync<DevHubPartner>(client, $"BusinessPartners/{id}", cancellationToken);
            if (partner.PartnerId != id)
            {
                throw new InvalidOperationException("O DevHub retornou dados inconsistentes do cliente.");
            }

            if (partner.Active && !string.IsNullOrWhiteSpace(partner.PartnerName))
            {
                partners.Add(partner);
            }
        }

        if (partners.Count == 0)
        {
            throw new UnauthorizedAccessException("Nenhum cliente ativo está disponível para este usuário.");
        }

        foreach (var entry in _sessions)
        {
            if (!entry.Value.Gate.Wait(0))
            {
                continue;
            }

            try
            {
                var sapKey = entry.Value.SapSessionKey;
                var hasSapSession = sapKey is not null &&
                    (_sapSessions.TryGet(sapKey, out _) || _sapSessions.TryGetForJob(sapKey, out _));
                if (entry.Value.ExpiresAt <= DateTimeOffset.UtcNow && !hasSapSession)
                {
                    _sessions.TryRemove(entry.Key, out _);
                }
            }
            finally
            {
                entry.Value.Gate.Release();
            }
        }

        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var session = new DevHubSession(login.Token.Token, login.Token.Expiration, profile.Username, partners, profile.Menus);
        _sessions[key] = session;
        return new DevHubLoginResult(key, partners);
    }

    public async Task<DevHubSelection> SelectAsync(string key, int partnerId, CancellationToken cancellationToken)
    {
        var session = GetSession(key);
        await session.Gate.WaitAsync(cancellationToken);
        try
        {
            await InvalidateSapAsync(session);
            session.Selection = null;
            EnsureCurrent(key, session);
            EnsureTokenValid(session);
            if (!session.Partners.Any(partner => partner.PartnerId == partnerId))
            {
                throw new UnauthorizedAccessException("Cliente não autorizado.");
            }

            using var client = CreateClient(session);
            var databasesTask = GetAsync<List<DevHubDatabase>>(client, $"BusinessPartners/{partnerId}/databases", cancellationToken);
            var serviceLayerTask = GetAsync<DevHubServiceLayer>(client, $"ServiceLayerInformations/partner/{partnerId}", cancellationToken);
            await Task.WhenAll(databasesTask, serviceLayerTask);
            var databases = await databasesTask;
            var serviceLayer = await serviceLayerTask;
            if (databases.Any(item => item.PartnerId != partnerId || string.IsNullOrWhiteSpace(item.NomeBase)) ||
                serviceLayer.PartnerId != partnerId || string.IsNullOrWhiteSpace(serviceLayer.UserName) ||
                !Uri.TryCreate(serviceLayer.Url, UriKind.Absolute, out var url) ||
                url.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(url.UserInfo))
            {
                throw new InvalidOperationException("O DevHub retornou dados inconsistentes para o cliente selecionado.");
            }

            EnsureCurrent(key, session);
            EnsureTokenValid(session);
            var selection = new DevHubSelection(partnerId, databases, serviceLayer);
            session.Selection = selection;
            return selection;
        }
        finally
        {
            session.Gate.Release();
        }
    }

    public async Task<LoginResponseDto> ConnectAsync(
        string key, int partnerId, int databaseId, string password,
        IAuthService authService, ISapSessionContextAccessor accessor, CancellationToken cancellationToken)
    {
        var session = GetSession(key);
        await session.Gate.WaitAsync(cancellationToken);
        try
        {
            EnsureCurrent(key, session);
            EnsureTokenValid(session);
            if (!session.Partners.Any(partner => partner.PartnerId == partnerId) ||
                session.Selection?.PartnerId != partnerId)
            {
                throw new UnauthorizedAccessException("Cliente não autorizado ou ainda não selecionado.");
            }

            var selection = session.Selection;
            var matches = selection.Databases.Where(item => item.Id == databaseId && item.PartnerId == partnerId).ToList();
            if (matches.Count != 1 || string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException("Selecione uma base autorizada e informe a senha SAP.");
            }

            await InvalidateSapAsync(session);
            var result = await authService.LoginSelectedAsync(
                selection.ServiceLayer.Url, matches[0].NomeBase, selection.ServiceLayer.UserName,
                password, session.UserName, cancellationToken);
            session.SapSessionKey = accessor.GetRequiredSessionKey();
            return result;
        }
        finally
        {
            session.Gate.Release();
        }
    }

    public async Task DisconnectAsync(string key)
    {
        var session = GetSession(key);
        await session.Gate.WaitAsync();
        try
        {
            EnsureCurrent(key, session);
            await InvalidateSapAsync(session);
            session.Selection = null;
        }
        finally
        {
            session.Gate.Release();
        }
    }

    public async Task LogoutAsync(string key)
    {
        if (!_sessions.TryGetValue(key, out var session))
        {
            return;
        }

        await session.Gate.WaitAsync();
        try
        {
            _sessions.TryRemove(key, out _);
            await InvalidateSapAsync(session);
        }
        finally
        {
            session.Gate.Release();
        }
    }

    private async Task InvalidateSapAsync(DevHubSession session)
    {
        var key = session.SapSessionKey;
        session.SapSessionKey = null;
        if (key is null)
        {
            return;
        }

        var logoutSession = _sapSessions.Invalidate(key);
        if (logoutSession is not null)
        {
            try
            {
                await _sapClient.LogoutAsync(logoutSession, CancellationToken.None);
            }
            catch
            {
                // The session is already invalidated locally.
            }
        }
    }

    private DevHubSession GetSession(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || !_sessions.TryGetValue(key, out var session))
        {
            throw new UnauthorizedAccessException("Sessão DevHub inválida. Entre novamente.");
        }

        return session;
    }

    private void EnsureCurrent(string key, DevHubSession session)
    {
        if (!_sessions.TryGetValue(key, out var current) || !ReferenceEquals(current, session))
        {
            throw new UnauthorizedAccessException("Sessão DevHub encerrada. Entre novamente.");
        }
    }

    private static void EnsureTokenValid(DevHubSession session)
    {
        if (session.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new UnauthorizedAccessException("Sessão DevHub expirada. Entre novamente.");
        }
    }

    private HttpClient CreateClient(DevHubSession session)
    {
        var client = _httpClientFactory.CreateClient(ClientName);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        return client;
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string path, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("O DevHub retornou uma resposta vazia.");
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            const string fallbackMessage = "Não foi possível consultar os dados no DevHub. Tente novamente.";
            string? message = null;

            try
            {
                var error = await response.Content.ReadFromJsonAsync<DevHubError>(JsonOptions, cancellationToken);
                message = error?.Message;
            }
            catch (JsonException)
            {
                // A resposta não segue o formato de erro do DevHub.
            }

            throw new DevHubNotFoundException(
                string.IsNullOrWhiteSpace(message) ? fallbackMessage : message);
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new UnauthorizedAccessException("Autorização DevHub recusada. Entre novamente.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Não foi possível consultar os dados no DevHub. Tente novamente.");
        }
    }

    private sealed class DevHubSession(
        string token, DateTimeOffset expiresAt, string userName, List<DevHubPartner> partners, JsonElement menus)
    {
        public string Token { get; } = token;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public string UserName { get; } = userName;
        public List<DevHubPartner> Partners { get; } = partners;
        public JsonElement Menus { get; } = menus;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public DevHubSelection? Selection { get; set; }
        public string? SapSessionKey { get; set; }
    }

    private sealed record DevHubError(string? Message);
    private sealed record DevHubLogin(DevHubToken? Token);
    private sealed record DevHubToken(string Token, DateTimeOffset Expiration);
    private sealed record DevHubProfile(string Username, bool IsActive, List<int>? BusinessPartnerIds, JsonElement Menus);
}

public sealed record DevHubPartner(int PartnerId, string PartnerName, bool Active);
public sealed record DevHubDatabase(int Id, string NomeBase, int PartnerId);
public sealed record DevHubServiceLayer(string Url, string UserName, int PartnerId, string PartnerName);
public sealed record DevHubSelection(int PartnerId, List<DevHubDatabase> Databases, DevHubServiceLayer ServiceLayer);
public sealed record DevHubLoginResult(string SessionKey, List<DevHubPartner> Partners);
