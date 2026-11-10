using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PodcastGenerator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SectionProofreading : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorrectionsJson",
                table: "EpisodeSections",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DraftJson",
                table: "EpisodeSections",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CorrectionsJson",
                table: "EpisodeSections");

            migrationBuilder.DropColumn(
                name: "DraftJson",
                table: "EpisodeSections");
        }
    }
}
