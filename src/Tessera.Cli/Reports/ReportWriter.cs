namespace Tessera.Cli.Reports;

public static class ReportWriter
{
    public static void Write(ReportData report, string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "architecture.md"), ReportRenderers.Architecture(report));
        File.WriteAllText(Path.Combine(directory, "dependencies.md"), ReportRenderers.Dependencies(report));
        File.WriteAllText(Path.Combine(directory, "impact.md"), ReportRenderers.Impact(report));
    }
}
