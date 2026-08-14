using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tessera.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIncludeIndexing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IncludeIndexing",
                table: "Repositories",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IncludeIndexing",
                table: "Repositories");
        }
    }
}
