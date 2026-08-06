using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tessera.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNodeEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NodeEmbeddings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Model = table.Column<string>(type: "text", nullable: false),
                    Dimensions = table.Column<int>(type: "integer", nullable: false),
                    Vector = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeEmbeddings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NodeEmbeddings_KnowledgeNodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "KnowledgeNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NodeEmbeddings_Snapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "Snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NodeEmbeddings_NodeId_Model",
                table: "NodeEmbeddings",
                columns: new[] { "NodeId", "Model" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NodeEmbeddings_SnapshotId_RepositoryId",
                table: "NodeEmbeddings",
                columns: new[] { "SnapshotId", "RepositoryId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NodeEmbeddings");
        }
    }
}
