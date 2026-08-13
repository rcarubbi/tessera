using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tessera.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AiSettingsPrimaryUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Deterministically keep only the most recently updated primary before the unique index is enforced.
            migrationBuilder.Sql("""
                UPDATE "AiSettings"
                SET "IsPrimary" = false
                WHERE "IsPrimary" = true
                  AND "Id" NOT IN (
                    SELECT "Id" FROM "AiSettings"
                    WHERE "IsPrimary" = true
                    ORDER BY "UpdatedAt" DESC, "Id"
                    LIMIT 1
                  );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AiSettings_IsPrimary",
                table: "AiSettings",
                column: "IsPrimary",
                unique: true,
                filter: "\"IsPrimary\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiSettings_IsPrimary",
                table: "AiSettings");
        }
    }
}
