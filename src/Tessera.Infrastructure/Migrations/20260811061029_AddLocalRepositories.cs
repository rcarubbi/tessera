using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tessera.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalRepositories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Repositories",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_CreatedBy",
                table: "Repositories",
                column: "CreatedBy");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Repositories_CreatedBy",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Repositories");
        }
    }
}
