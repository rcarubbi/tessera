using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tessera.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAiProviderPrimary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPrimary",
                table: "AiSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""UPDATE "AiSettings" SET "IsPrimary" = true""");

            migrationBuilder.CreateIndex(
                name: "IX_AiSettings_ProviderName",
                table: "AiSettings",
                column: "ProviderName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiSettings_ProviderName",
                table: "AiSettings");

            migrationBuilder.DropColumn(
                name: "IsPrimary",
                table: "AiSettings");
        }
    }
}
