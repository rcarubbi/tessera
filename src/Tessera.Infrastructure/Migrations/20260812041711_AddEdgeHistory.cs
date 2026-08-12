using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tessera.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEdgeHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EdgeHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromKey = table.Column<string>(type: "text", nullable: false),
                    ToKey = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    IntroducedSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntroducedCommitSha = table.Column<string>(type: "text", nullable: false),
                    IntroducedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Live = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EdgeHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EdgeHistories_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EdgeHistories_RepositoryId_FromKey_ToKey_Type",
                table: "EdgeHistories",
                columns: new[] { "RepositoryId", "FromKey", "ToKey", "Type" },
                unique: true,
                filter: "\"Live\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_EdgeHistories_RepositoryId_Live",
                table: "EdgeHistories",
                columns: new[] { "RepositoryId", "Live" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EdgeHistories");
        }
    }
}
