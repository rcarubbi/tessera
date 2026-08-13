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
    public DbSet<EdgeHistory> EdgeHistories => Set<EdgeHistory>();
    public DbSet<NodeEmbedding> NodeEmbeddings => Set<NodeEmbedding>();
    public DbSet<ProjectOverview> ProjectOverviews => Set<ProjectOverview>();
    public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();
    public DbSet<GitHubUser> GitHubUsers => Set<GitHubUser>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<AiSettings> AiSettings => Set<AiSettings>();
    public DbSet<PullRequestReview> PullRequestReviews => Set<PullRequestReview>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Repository>(e =>
        {
            e.HasIndex(r => r.FullName).IsUnique();
            e.HasIndex(r => r.InstallationId);
            e.HasIndex(r => new { r.Status, r.UpdatedAt });
            e.Property(r => r.CreatedBy).HasMaxLength(256);
            e.HasIndex(r => r.CreatedBy);
            e.Property(r => r.RulesYaml).HasColumnType("text");
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
            e.Property(n => n.ClassDiagram).HasColumnType("text");
            e.Property(n => n.SequenceDiagram).HasColumnType("text");
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

        modelBuilder.Entity<EdgeHistory>(e =>
        {
            e.HasIndex(h => new { h.RepositoryId, h.FromKey, h.ToKey, h.Type }).IsUnique().HasFilter("\"Live\" = true");
            e.HasIndex(h => new { h.RepositoryId, h.Live });
            e.HasOne<Repository>()
                .WithMany()
                .HasForeignKey(h => h.RepositoryId)
                .OnDelete(DeleteBehavior.Cascade);
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

        modelBuilder.Entity<ProjectOverview>(e =>
        {
            e.HasIndex(o => new { o.SnapshotId }).IsUnique();
            e.Property(o => o.Content).HasColumnType("text");
            e.HasOne(o => o.Repository)
                .WithMany()
                .HasForeignKey(o => o.RepositoryId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(o => o.Snapshot)
                .WithMany()
                .HasForeignKey(o => o.SnapshotId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ConversationMessage>(e =>
        {
            e.HasIndex(m => new { m.RepositoryId, m.CreatedAt });
            e.Property(m => m.Content).HasColumnType("text");
            e.Property(m => m.CitationsJson).HasColumnType("text");
            e.Property(m => m.WarningsJson).HasColumnType("text");
            e.HasOne(m => m.Repository)
                .WithMany()
                .HasForeignKey(m => m.RepositoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AiSettings>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.BaseUrl).HasMaxLength(512);
            e.Property(s => s.ApiKey).HasMaxLength(512);
            e.HasIndex(s => s.ProviderName).IsUnique();
            e.HasIndex(s => s.IsPrimary).IsUnique().HasFilter("\"IsPrimary\" = true");
        });

        modelBuilder.Entity<PullRequestReview>(e =>
        {
            e.HasIndex(r => new { r.RepositoryId, r.PrNumber, r.HeadSha }).IsUnique();
            e.HasIndex(r => new { r.RepositoryId, r.Status });
            e.Property(r => r.CommentBody).HasColumnType("text");
            e.HasOne<Repository>()
                .WithMany()
                .HasForeignKey(r => r.RepositoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
