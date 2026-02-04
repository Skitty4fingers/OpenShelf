using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenShelf.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffPickAndLikeHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsStaffPick",
                table: "Recommendations",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "RecommendationLikes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RecommendationId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationLikes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecommendationLikes_Recommendations_RecommendationId",
                        column: x => x.RecommendationId,
                        principalTable: "Recommendations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationLikes_RecommendationId",
                table: "RecommendationLikes",
                column: "RecommendationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecommendationLikes");

            migrationBuilder.DropColumn(
                name: "IsStaffPick",
                table: "Recommendations");
        }
    }
}
