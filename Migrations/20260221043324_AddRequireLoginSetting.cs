using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenShelf.Migrations
{
    /// <inheritdoc />
    public partial class AddRequireLoginSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequireLogin",
                table: "SiteSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "SiteSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "RequireLogin",
                value: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequireLogin",
                table: "SiteSettings");
        }
    }
}
