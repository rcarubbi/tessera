namespace Tessera.Domain.Enums;

public enum ProcessingStatus
{
    Pending,
    Cloning,
    Parsing,
    Analyzing,
    Indexing,
    Completed,
    Failed
}
