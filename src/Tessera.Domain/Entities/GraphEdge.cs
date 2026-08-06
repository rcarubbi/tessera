using Tessera.Domain.Enums;

namespace Tessera.Domain.Entities;

public class GraphEdge
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public Guid SnapshotId { get; set; }

    public Guid FromNodeId { get; set; }
    public string FromKey { get; set; } = "";
    public Guid ToNodeId { get; set; }
    public string ToKey { get; set; } = "";

    public EdgeType Type { get; set; }
    public string? Evidence { get; set; }
    public double Confidence { get; set; } = 1.0;
    public bool IsStatic { get; set; }
    public int Depth { get; set; }
}
