using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Tessera.Infrastructure.Ai;

public sealed class TokenBudgetTracker
{
    private readonly AiOptions _options;
    private readonly ConcurrentDictionary<(long RepositoryId, string DayKey), long> _used = new();

    public TokenBudgetTracker(IOptions<AiOptions> options)
    {
        _options = options.Value;
    }

    public bool TryAllocate(long repositoryId, long tokens, DateTimeOffset now)
    {
        var key = (repositoryId, DayKey(now));
        var used = _used.GetOrAdd(key, _ => 0L);
        if (used + tokens > _options.DailyBudgetTokens)
        {
            return false;
        }
        _used[key] = used + tokens;
        return true;
    }

    public long Used(long repositoryId, DateTimeOffset now) =>
        _used.TryGetValue((repositoryId, DayKey(now)), out var used) ? used : 0;

    private static string DayKey(DateTimeOffset now) => now.UtcDateTime.ToString("yyyyMMdd");
}
