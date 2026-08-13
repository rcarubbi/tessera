using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Tessera.Infrastructure.GitHub;

public sealed class GitHubOptions
{
    public string AppId { get; set; } = "";
    public string PrivateKeyPath { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string ApiUrl { get; set; } = "https://api.github.com";
    public string AppUrl { get; set; } = "";
}

public sealed record GitHubRepoInfo(
    long Id,
    string FullName,
    string Owner,
    string Name,
    string DefaultBranch,
    string CloneUrl);

public interface IGitHubAppClient
{
    Task<string> CreateInstallationAccessTokenAsync(long installationId, CancellationToken ct = default);
    Task<IReadOnlyList<GitHubRepoInfo>> ListInstallationRepositoriesAsync(long installationId, string token, CancellationToken ct = default);
    Task<long> PostPrCommentAsync(long installationId, string owner, string repo, int prNumber, string body, CancellationToken ct = default);
    Task DeletePrCommentAsync(long installationId, string owner, string repo, long commentId, CancellationToken ct = default);
}

public sealed class GitHubApiException(string message) : Exception(message);

public sealed class GitHubAppClient : IGitHubAppClient
{
    private readonly HttpClient _http;
    private readonly GitHubOptions _options;

    public GitHubAppClient(IHttpClientFactory factory, IOptions<GitHubOptions> options)
    {
        _http = factory.CreateClient(nameof(GitHubAppClient));
        _options = options.Value;
        _http.BaseAddress = new Uri(_options.ApiUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("tessera");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task<string> CreateInstallationAccessTokenAsync(long installationId, CancellationToken ct = default)
    {
        var jwt = CreateAppJwt();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"app/installations/{installationId}/access_tokens");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<InstallationTokenResponse>(ct);
        return payload?.Token ?? throw new InvalidOperationException("GitHub returned no installation token.");
    }

    public async Task<IReadOnlyList<GitHubRepoInfo>> ListInstallationRepositoriesAsync(long installationId, string token, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "installation/repositories");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<InstallationRepositoriesResponse>(ct);
        return payload?.Repositories
            .Select(r => new GitHubRepoInfo(
                r.Id,
                r.FullName,
                r.Owner?.Login ?? "",
                r.Name,
                r.DefaultBranch ?? "main",
                r.CloneUrl ?? ""))
            .ToList() ?? new List<GitHubRepoInfo>();
    }

    public async Task<long> PostPrCommentAsync(
        long installationId,
        string owner,
        string repo,
        int prNumber,
        string body,
        CancellationToken ct = default)
    {
        var token = await CreateInstallationAccessTokenAsync(installationId, ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"repos/{owner}/{repo}/issues/{prNumber}/comments")
        {
            Content = JsonContent.Create(new { body })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw await GitHubApiErrorAsync("comment post", response, ct);
        }
        var comment = await response.Content.ReadFromJsonAsync<CommentResponse>(ct);
        return comment?.Id ?? 0;
    }

    public async Task DeletePrCommentAsync(
        long installationId,
        string owner,
        string repo,
        long commentId,
        CancellationToken ct = default)
    {
        var token = await CreateInstallationAccessTokenAsync(installationId, ct);
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"repos/{owner}/{repo}/issues/comments/{commentId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw await GitHubApiErrorAsync("comment delete", response, ct);
        }
    }

    private string CreateAppJwt()
    {
        var pem = File.ReadAllText(_options.PrivateKeyPath);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);

        var now = DateTimeOffset.UtcNow;
        var header = Base64Url(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\"}"));
        var payload = Base64Url(Encoding.UTF8.GetBytes(
            $"{{\"iat\":{now.ToUnixTimeSeconds()},\"exp\":{now.AddMinutes(9).ToUnixTimeSeconds()},\"iss\":\"{_options.AppId}\"}}"));
        var signingInput = $"{header}.{payload}";
        var signature = rsa.SignData(Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{signingInput}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static async Task<GitHubApiException> GitHubApiErrorAsync(string action, HttpResponseMessage response, CancellationToken ct)
    {
        var details = await response.Content.ReadAsStringAsync(ct);
        var message = $"GitHub {action} failed: {(int)response.StatusCode} {response.ReasonPhrase} {details}";
        return new GitHubApiException(message.Length <= 2000 ? message : message[..2000]);
    }

    private sealed class CommentResponse
    {
        public long Id { get; set; }
    }

    private sealed class InstallationTokenResponse
    {
        public string? Token { get; set; }
        public string? ExpiresAt { get; set; }
    }

    private sealed class InstallationRepositoriesResponse
    {
        public List<InstallationRepository> Repositories { get; set; } = new();
    }

    private sealed class InstallationRepository
    {
        public long Id { get; set; }
        public string FullName { get; set; } = "";
        public string Name { get; set; } = "";
        public string? DefaultBranch { get; set; }
        public string? CloneUrl { get; set; }
        public InstallationRepositoryOwner? Owner { get; set; }
    }

    private sealed class InstallationRepositoryOwner
    {
        public string? Login { get; set; }
    }
}
