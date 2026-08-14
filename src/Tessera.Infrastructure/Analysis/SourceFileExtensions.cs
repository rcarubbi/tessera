namespace Tessera.Infrastructure.Analysis;

public static class SourceFileExtensions
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        "cs", "java", "js", "jsx", "mjs", "cjs", "ts", "tsx", "py", "go", "php", "rb"
    };

    public static bool HasSupportedExtension(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return Supported.Contains(ext);
    }
}
