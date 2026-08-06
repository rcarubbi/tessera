using Tessera.Domain.Entities;

namespace Tessera.Domain.Entities;

public class NodeEmbedding
{
    public Guid Id { get; set; }
    public Guid NodeId { get; set; }
    public Guid SnapshotId { get; set; }
    public Guid RepositoryId { get; set; }
    public string Model { get; set; } = "";
    public int Dimensions { get; set; }
    public byte[] Vector { get; set; } = Array.Empty<byte>();
    public DateTimeOffset CreatedAt { get; set; }

    public KnowledgeNode? Node { get; set; }
    public Snapshot? Snapshot { get; set; }
}
