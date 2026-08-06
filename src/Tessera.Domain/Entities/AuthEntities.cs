namespace Tessera.Domain.Entities;

public class GitHubUser
{
    public Guid Id { get; set; }
    public string Login { get; set; } = "";
    public string Name { get; set; } = "";
    public string AvatarUrl { get; set; } = "";
    public string InstallationIdsJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class AuthSession
{
    public Guid Id { get; set; }
    public string Token { get; set; } = "";
    public Guid GitHubUserId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
