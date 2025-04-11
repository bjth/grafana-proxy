using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrafanaProxy.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTimestamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Created",
                table: "ApiKeys",
                newName: "LastModifiedAt");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Tenants",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModifiedAt",
                table: "Tenants",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "TenantDashboardPermissions",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModifiedAt",
                table: "TenantDashboardPermissions",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "ApiKeys",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "LastModifiedAt",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "TenantDashboardPermissions");

            migrationBuilder.DropColumn(
                name: "LastModifiedAt",
                table: "TenantDashboardPermissions");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "ApiKeys");

            migrationBuilder.RenameColumn(
                name: "LastModifiedAt",
                table: "ApiKeys",
                newName: "Created");
        }
    }
}
