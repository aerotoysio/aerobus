using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AeroBus.Core.Identity
{
    /// <summary>
    /// Raised when the Keycloak Admin API rejects a call; carries the upstream
    /// status so endpoints can distinguish conflicts from hard failures.
    /// </summary>
    public sealed class KeycloakApiException(HttpStatusCode status, string message)
        : Exception($"Keycloak admin API: {(int)status} {message}")
    {
        public HttpStatusCode Status { get; } = status;
    }

    // Wire representations (Keycloak Admin REST, camelCase JSON).
    public sealed record KcOrganization(
        string Id,
        string Name,
        string? Alias,
        bool Enabled,
        List<KcOrgDomain>? Domains,
        Dictionary<string, List<string>>? Attributes);
    public sealed record KcOrgDomain(string Name, bool Verified);
    public sealed record KcRole(string Id, string Name, string? Description);
    public sealed record KcUser(string Id, string Username, string? Email, string? FirstName, string? LastName, bool Enabled);

    /// <summary>
    /// Minimal typed client over the Keycloak Admin REST API, authenticated with
    /// the aerobus service account (client_credentials, token cached until expiry).
    /// Registered as a singleton; requests go through IHttpClientFactory.
    /// </summary>
    public sealed class KeycloakAdminClient(IHttpClientFactory httpFactory, IOptions<KeycloakOptions> options)
    {
        public const string HttpClientName = "keycloak-admin";

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly KeycloakOptions _opts = options.Value;
        private readonly SemaphoreSlim _tokenLock = new(1, 1);
        private string? _token;
        private DateTimeOffset _tokenExpires = DateTimeOffset.MinValue;

        // ---- service-account token ------------------------------------------

        private async Task<string> GetTokenAsync(CancellationToken ct)
        {
            if (_token is not null && DateTimeOffset.UtcNow < _tokenExpires) return _token;

            await _tokenLock.WaitAsync(ct);
            try
            {
                if (_token is not null && DateTimeOffset.UtcNow < _tokenExpires) return _token;

                var http = httpFactory.CreateClient(HttpClientName);
                var res = await http.PostAsync(
                    $"{_opts.Authority}/protocol/openid-connect/token",
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["grant_type"] = "client_credentials",
                        ["client_id"] = _opts.ClientId,
                        ["client_secret"] = _opts.ClientSecret,
                    }),
                    ct);
                if (!res.IsSuccessStatusCode)
                    throw new KeycloakApiException(res.StatusCode, "service-account token request failed");

                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
                _token = doc.RootElement.GetProperty("access_token").GetString();
                var expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();
                _tokenExpires = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn - 30));
                return _token!;
            }
            finally
            {
                _tokenLock.Release();
            }
        }

        private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
        {
            var http = httpFactory.CreateClient(HttpClientName);
            var req = new HttpRequestMessage(method, $"{_opts.AdminBase}{path}");
            req.Headers.Authorization = new("Bearer", await GetTokenAsync(ct));
            if (body is not null) req.Content = JsonContent.Create(body, options: Json);
            return await http.SendAsync(req, ct);
        }

        private async Task<T> GetAsync<T>(string path, CancellationToken ct)
        {
            var res = await SendAsync(HttpMethod.Get, path, null, ct);
            if (!res.IsSuccessStatusCode)
                throw new KeycloakApiException(res.StatusCode, await res.Content.ReadAsStringAsync(ct));
            return (await res.Content.ReadFromJsonAsync<T>(Json, ct))!;
        }

        private async Task EnsureAsync(HttpMethod method, string path, object? body, CancellationToken ct)
        {
            var res = await SendAsync(method, path, body, ct);
            if (!res.IsSuccessStatusCode)
                throw new KeycloakApiException(res.StatusCode, await res.Content.ReadAsStringAsync(ct));
        }

        // ---- organisations ---------------------------------------------------

        public Task<List<KcOrganization>> ListOrganizationsAsync(CancellationToken ct = default) =>
            GetAsync<List<KcOrganization>>("/organizations?max=500", ct);

        public async Task<KcOrganization?> GetOrganizationByAliasAsync(string alias, CancellationToken ct = default)
        {
            var hits = await GetAsync<List<KcOrganization>>($"/organizations?search={Uri.EscapeDataString(alias)}", ct);
            return hits.FirstOrDefault(o =>
                string.Equals(o.Alias, alias, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(o.Name, alias, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<KcOrganization> CreateOrganizationAsync(string name, string alias, string domain, CancellationToken ct = default)
        {
            await EnsureAsync(HttpMethod.Post, "/organizations", new
            {
                name,
                alias,
                enabled = true,
                domains = new[] { new { name = domain, verified = true } },
            }, ct);
            return await GetOrganizationByAliasAsync(alias, ct)
                ?? throw new KeycloakApiException(HttpStatusCode.InternalServerError, $"organization '{alias}' vanished after create");
        }

        public Task<KcOrganization> GetOrganizationAsync(string orgId, CancellationToken ct = default) =>
            GetAsync<KcOrganization>($"/organizations/{orgId}", ct);

        /// <summary>Full-representation update — send name/alias/domains along with any attribute changes.</summary>
        public Task UpdateOrganizationAsync(string orgId, object representation, CancellationToken ct = default) =>
            EnsureAsync(HttpMethod.Put, $"/organizations/{orgId}", representation, ct);

        public Task<List<KcUser>> GetOrganizationMembersAsync(string orgId, CancellationToken ct = default) =>
            GetAsync<List<KcUser>>($"/organizations/{orgId}/members?max=500", ct);

        public Task AddOrganizationMemberAsync(string orgId, string userId, CancellationToken ct = default) =>
            EnsureAsync(HttpMethod.Post, $"/organizations/{orgId}/members", userId, ct);

        // ---- users -----------------------------------------------------------

        public async Task<KcUser?> GetUserByUsernameAsync(string username, CancellationToken ct = default)
        {
            var hits = await GetAsync<List<KcUser>>($"/users?username={Uri.EscapeDataString(username)}&exact=true", ct);
            return hits.FirstOrDefault();
        }

        public Task<KcUser> GetUserByIdAsync(string userId, CancellationToken ct = default) =>
            GetAsync<KcUser>($"/users/{userId}", ct);

        public async Task<KcUser> CreateUserAsync(
            string email, string? firstName, string? lastName, string password, CancellationToken ct = default)
        {
            await EnsureAsync(HttpMethod.Post, "/users", new
            {
                username = email,
                email,
                firstName,
                lastName,
                enabled = true,
                emailVerified = true,
                credentials = new[] { new { type = "password", value = password, temporary = false } },
            }, ct);
            return await GetUserByUsernameAsync(email, ct)
                ?? throw new KeycloakApiException(HttpStatusCode.InternalServerError, $"user '{email}' vanished after create");
        }

        public Task SetUserEnabledAsync(string userId, bool enabled, CancellationToken ct = default) =>
            EnsureAsync(HttpMethod.Put, $"/users/{userId}", new { enabled }, ct);

        public Task UpdateUserNamesAsync(string userId, string? firstName, string? lastName, CancellationToken ct = default) =>
            EnsureAsync(HttpMethod.Put, $"/users/{userId}", new { firstName, lastName }, ct);

        public Task ResetPasswordAsync(string userId, string password, CancellationToken ct = default) =>
            EnsureAsync(HttpMethod.Put, $"/users/{userId}/reset-password",
                new { type = "password", value = password, temporary = false }, ct);

        // ---- realm roles -----------------------------------------------------

        public Task<List<KcRole>> ListRealmRolesAsync(CancellationToken ct = default) =>
            GetAsync<List<KcRole>>("/roles", ct);

        public Task<List<KcRole>> GetUserRealmRolesAsync(string userId, CancellationToken ct = default) =>
            GetAsync<List<KcRole>>($"/users/{userId}/role-mappings/realm", ct);

        public Task AddUserRealmRolesAsync(string userId, IEnumerable<KcRole> roles, CancellationToken ct = default) =>
            EnsureAsync(HttpMethod.Post, $"/users/{userId}/role-mappings/realm", roles, ct);

        public Task RemoveUserRealmRolesAsync(string userId, IEnumerable<KcRole> roles, CancellationToken ct = default) =>
            EnsureAsync(HttpMethod.Delete, $"/users/{userId}/role-mappings/realm", roles, ct);
    }
}
