using Microsoft.EntityFrameworkCore;
using Tessera.Domain.Entities;

namespace Tessera.Infrastructure.Data;

public class TesseraDbContext : DbContext
{
    public TesseraDbContext(DbContextOptions<TesseraDbContext> options) : base(options)
    {
    }

    public DbSet<Repository> Repositories => Set<Repository>();
    public DbSet<GitHubInstallation> GitHubInstallations => Set<GitHubInstallation>();
    public DbSet<Snapshot> Snapshots => Set<Snapshot>();
    public DbSet<KnowledgeNode> KnowledgeNodes => Set<KnowledgeNode>();
    public DbSet<KnowledgeNodeProvenance> KnowledgeNodeProvenances => Set<KnowledgeNodeProvenance>();
    public DbSet<GraphEdge> GraphEdges => Set<GraphEdge>();
    public DbSet<NodeEmbedding> NodeEmbeddings => Set<NodeEmbedding>();
    public DbSet<GitHubUser> GitHubUsers => Set<GitHubUser>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Repository>(e =>
        {
            e.HasIndex(r => r.FullName).IsUnique();
            e.HasIndex(r => r.InstallationId);
        });

        modelBuilder.Entity<GitHubInstallation>(e =>
        {
            e.HasKey(i => i.Id);
            e.HasIndex(i => i.AccountId).IsUnique();
        });

        modelBuilder.Entity<GitHubUser>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Login).IsUnique();
        });

        modelBuilder.Entity<AuthSession>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => s.Token).IsUnique();
            e.HasIndex(s => s.GitHubUserId);
            e.HasOne<GitHubUser>()
                .WithMany()
                .HasForeignKey(s => s.GitHubUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Snapshot>(e =>
        {
            e.HasIndex(s => new { s.RepositoryId, s.CommitSha }).IsUnique();
            e.HasOne(s => s.Repository)
                .WithMany()
                .HasForeignKey(s => s.RepositoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<KnowledgeNode>(e =>
        {
            e.HasIndex(n => new { n.RepositoryId, n.SnapshotId, n.Key }).IsUnique();
            e.HasIndex(n => n.StructuralHash);
            e.HasIndex(n => n.SemanticHash);
            e.HasIndex(n => n.ReviewStatus);
            e.Property(n => n.Content).HasColumnType("text");
            e.HasOne(n => n.Repository)
                .WithMany()
                .HasForeignKey(n => n.RepositoryId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(n => n.Snapshot)
                .WithMany()
                .HasForeignKey(n => n.SnapshotId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GraphEdge>(e =>
        {
            e.HasIndex(edge => new { edge.SnapshotId, edge.FromKey });
            e.HasIndex(edge => new { edge.SnapshotId, edge.ToKey });
            e.HasIndex(edge => new { edge.SnapshotId, edge.Type });
        });

        modelBuilder.Entity<KnowledgeNodeProvenance>(e =>
        {
            e.HasIndex(p => new { p.NodeId, p.GeneratedAt });
            e.HasIndex(p => p.PromptVersion);
        });

        modelBuilder.Entity<NodeEmbedding>(e =>
        {
            e.HasIndex(n => new { n.NodeId, n.Model }).IsUnique();
            e.HasIndex(n => new { n.SnapshotId, n.RepositoryId });
            e.HasOne(n => n.Snapshot)
                .WithMany()
                .HasForeignKey(n => n.SnapshotId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
