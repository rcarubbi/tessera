using Tessera.Domain.Entities;

namespace Tessera.Infrastructure.Auth;

public static class RepositoryAccess
{
    public static bool CanAccess(AccessContext access, Repository repo)
        => access.IsAdmin || access.InstallationIds.Contains(repo.InstallationId);

    public static IEnumerable<Repository> Scope(IEnumerable<Repository> repos, AccessContext access)
        => access.IsAdmin ? repos : repos.Where(r => access.InstallationIds.Contains(r.InstallationId));
}
