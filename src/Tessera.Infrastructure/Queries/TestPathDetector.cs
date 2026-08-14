namespace Tessera.Infrastructure.Queries;

public static class TestPathDetector
{
    public static bool IsTestPath(string path)
    {
        var lower = path.ToLowerInvariant();
        var segments = lower.Split('/');
        if (segments.Any(s => s is "test" or "tests"))
        {
            return true;
        }

        var fileName = Path.GetFileNameWithoutExtension(segments[^1]);
        return fileName.StartsWith("test", StringComparison.Ordinal)
            || fileName.EndsWith("test", StringComparison.Ordinal)
            || fileName.EndsWith("tests", StringComparison.Ordinal)
            || fileName.StartsWith("spec", StringComparison.Ordinal)
            || fileName.EndsWith("spec", StringComparison.Ordinal)
            || fileName.Contains(".test", StringComparison.Ordinal)
            || fileName.Contains(".spec", StringComparison.Ordinal);
    }
}
