using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenShelf.Migrations
{
    /// <inheritdoc />
    public partial class AddExtendedCsvFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Abridged",
                table: "RecommendationItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Asin",
                table: "RecommendationItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AverageRating",
                table: "RecommendationItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BookUrl",
                table: "RecommendationItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Copyright",
                table: "RecommendationItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "RecommendationItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductId",
                table: "RecommendationItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PurchaseDate",
                table: "RecommendationItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RatingCount",
                table: "RecommendationItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReleaseDate",
                table: "RecommendationItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeriesName",
                table: "RecommendationItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeriesSequence",
                table: "RecommendationItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeriesUrl",
                table: "RecommendationItems",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Abridged",
                table: "RecommendationItems");

            migrationBuilder.DropColumn(
                name: "Asin",
                table: "RecommendationItems");

            migrationBuilder.DropColumn(
                name: "AverageRating",
                table: "RecommendationItems");

            migrationBuilder.DropColumn(
                name: "BookUrl",
                table: "RecommendationItems");

            migrationBuilder.DropColumn(
                name: "Copyright",
                table: "RecommendationItems");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "RecommendationItems");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "RecommendationItems");

            migrationBuilder.DropColumn(
                name: "PurchaseDate",
                table: "RecommendationItems");

            migrationBuilder.DropColumn(
                name: "RatingCount",
                table: "RecommendationItems");

            migrationBuilder.DropColumn(
                name: "ReleaseDate",
                table: "RecommendationItems");

            migrationBuilder.DropColumn(
                name: "SeriesName",
                table: "RecommendationItems");

            migrationBuilder.DropColumn(
                name: "SeriesSequence",
                table: "RecommendationItems");

            migrationBuilder.DropColumn(
                name: "SeriesUrl",
                table: "RecommendationItems");
        }
    }
}
