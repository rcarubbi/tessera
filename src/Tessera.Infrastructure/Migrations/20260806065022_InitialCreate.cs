using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Tessera.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GitHubInstallations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    AccountLogin = table.Column<string>(type: "text", nullable: false),
                    AccessToken = table.Column<string>(type: "text", nullable: true),
                    TokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubInstallations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GraphEdges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromKey = table.Column<string>(type: "text", nullable: false),
                    ToNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToKey = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Evidence = table.Column<string>(type: "text", nullable: true),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    IsStatic = table.Column<bool>(type: "boolean", nullable: false),
                    Depth = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GraphEdges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeNodeProvenances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommitSha = table.Column<string>(type: "text", nullable: false),
                    Model = table.Column<string>(type: "text", nullable: false),
                    PromptVersion = table.Column<string>(type: "text", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EditedBy = table.Column<string>(type: "text", nullable: true),
                    EditedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PreviousSemanticHash = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeNodeProvenances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Repositories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GitHubId = table.Column<long>(type: "bigint", nullable: false),
                    Owner = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    FullName = table.Column<string>(type: "text", nullable: false),
                    DefaultBranch = table.Column<string>(type: "text", nullable: false),
                    CloneUrl = table.Column<string>(type: "text", nullable: true),
                    InstallationId = table.Column<long>(type: "bigint", nullable: false),
                    IsConnected = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LastProcessedCommit = table.Column<string>(type: "text", nullable: true),
                    LastSnapshotAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NodeCount = table.Column<int>(type: "integer", nullable: false),
                    EdgeCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Repositories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommitSha = table.Column<string>(type: "text", nullable: false),
                    RootHash = table.Column<string>(type: "text", nullable: false),
                    NodeCount = table.Column<int>(type: "integer", nullable: false),
                    EdgeCount = table.Column<int>(type: "integer", nullable: false),
                    ParentCommitSha = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Snapshots_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeNodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "text", nullable: false),
                    Path = table.Column<string>(type: "text", nullable: false),
                    Symbol = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Language = table.Column<string>(type: "text", nullable: false),
                    StartLine = table.Column<int>(type: "integer", nullable: false),
                    EndLine = table.Column<int>(type: "integer", nullable: false),
                    StructuralHash = table.Column<string>(type: "text", nullable: false),
                    SemanticHash = table.Column<string>(type: "text", nullable: false),
                    ParentSemanticHash = table.Column<string>(type: "text", nullable: true),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    ReviewStatus = table.Column<int>(type: "integer", nullable: false),
                    CommitSha = table.Column<string>(type: "text", nullable: false),
                    Model = table.Column<string>(type: "text", nullable: true),
                    PromptVersion = table.Column<string>(type: "text", nullable: true),
                    AnalyzedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeNodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KnowledgeNodes_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KnowledgeNodes_Snapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "Snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GitHubInstallations_AccountId",
                table: "GitHubInstallations",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GraphEdges_SnapshotId_FromKey",
                table: "GraphEdges",
                columns: new[] { "SnapshotId", "FromKey" });

            migrationBuilder.CreateIndex(
                name: "IX_GraphEdges_SnapshotId_ToKey",
                table: "GraphEdges",
                columns: new[] { "SnapshotId", "ToKey" });

            migrationBuilder.CreateIndex(
                name: "IX_GraphEdges_SnapshotId_Type",
                table: "GraphEdges",
                columns: new[] { "SnapshotId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeNodeProvenances_NodeId_GeneratedAt",
                table: "KnowledgeNodeProvenances",
                columns: new[] { "NodeId", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeNodeProvenances_PromptVersion",
                table: "KnowledgeNodeProvenances",
                column: "PromptVersion");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeNodes_RepositoryId_SnapshotId_Key",
                table: "KnowledgeNodes",
                columns: new[] { "RepositoryId", "SnapshotId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeNodes_ReviewStatus",
                table: "KnowledgeNodes",
                column: "ReviewStatus");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeNodes_SemanticHash",
                table: "KnowledgeNodes",
                column: "SemanticHash");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeNodes_SnapshotId",
                table: "KnowledgeNodes",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeNodes_StructuralHash",
                table: "KnowledgeNodes",
                column: "StructuralHash");

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_FullName",
                table: "Repositories",
                column: "FullName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_InstallationId",
                table: "Repositories",
                column: "InstallationId");

            migrationBuilder.CreateIndex(
                name: "IX_Snapshots_RepositoryId_CommitSha",
                table: "Snapshots",
                columns: new[] { "RepositoryId", "CommitSha" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GitHubInstallations");

            migrationBuilder.DropTable(
                name: "GraphEdges");

            migrationBuilder.DropTable(
                name: "KnowledgeNodeProvenances");

            migrationBuilder.DropTable(
                name: "KnowledgeNodes");

            migrationBuilder.DropTable(
                name: "Snapshots");

            migrationBuilder.DropTable(
                name: "Repositories");
        }
    }
}
