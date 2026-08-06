namespace Tessera.Domain.Entities;

public class GitHubInstallation
{
    public long Id { get; set; }
    public long AccountId { get; set; }
    public string AccountLogin { get; set; } = "";
    public string? AccessToken { get; set; }
    public DateTimeOffset? TokenExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
