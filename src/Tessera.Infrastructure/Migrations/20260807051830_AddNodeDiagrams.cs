using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tessera.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNodeDiagrams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClassDiagram",
                table: "KnowledgeNodes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SequenceDiagram",
                table: "KnowledgeNodes",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClassDiagram",
                table: "KnowledgeNodes");

            migrationBuilder.DropColumn(
                name: "SequenceDiagram",
                table: "KnowledgeNodes");
        }
    }
}
