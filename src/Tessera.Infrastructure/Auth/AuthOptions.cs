namespace Tessera.Infrastructure.Auth;

public sealed class AuthOptions
{
    public string Admins { get; set; } = "";
    public int SessionLifetimeHours { get; set; } = 12;

    public IReadOnlyList<string> AdminLogins =>
        Admins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
