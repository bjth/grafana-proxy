using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrafanaProxy.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveApiKeyUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApiKeys_KeyValue",
                table: "ApiKeys");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_KeyValue",
                table: "ApiKeys",
                column: "KeyValue",
                unique: true);
        }
    }
}
