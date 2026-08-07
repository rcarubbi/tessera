namespace Tessera.Domain.Entities;

public class ConversationMessage
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
    public string? Mode { get; set; }
    public string CitationsJson { get; set; } = "[]";
    public string WarningsJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Repository? Repository { get; set; }
}
