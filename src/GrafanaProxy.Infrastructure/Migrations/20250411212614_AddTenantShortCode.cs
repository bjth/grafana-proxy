using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrafanaProxy.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantShortCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShortCode",
                table: "Tenants",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            // Populate existing ShortCodes before creating the unique index
            // Using Id ensures uniqueness for existing rows. Adjust if Id is not sequential or if another unique value is preferred.
            migrationBuilder.Sql("UPDATE Tenants SET ShortCode = 'TENANT_' || Id WHERE ShortCode = ''");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Name",
                table: "Tenants",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_ShortCode",
                table: "Tenants",
                column: "ShortCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tenants_Name",
                table: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_Tenants_ShortCode",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ShortCode",
                table: "Tenants");
        }
    }
}
