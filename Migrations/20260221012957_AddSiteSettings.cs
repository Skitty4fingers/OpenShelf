using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenShelf.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SiteSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GoogleBooksApiKey = table.Column<string>(type: "TEXT", nullable: true),
                    EnableGoogleBooks = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableOpenLibrary = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableAudible = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableGoodreads = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableChat = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnablePublicImport = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnablePublicMetadataRefresh = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableGetThisBookLinks = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteSettings", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "SiteSettings",
                columns: new[] { "Id", "EnableAudible", "EnableChat", "EnableGetThisBookLinks", "EnableGoodreads", "EnableGoogleBooks", "EnableOpenLibrary", "EnablePublicImport", "EnablePublicMetadataRefresh", "GoogleBooksApiKey" },
                values: new object[] { 1, true, true, true, true, true, true, true, true, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SiteSettings");
        }
    }
}
