using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PodcastGenerator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SegmentTranslationLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Translation",
                table: "TranscriptSegments",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TranslationLanguage",
                table: "TranscriptSegments",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TranslationLanguage",
                table: "TranscriptSegments");

            migrationBuilder.AlterColumn<string>(
                name: "Translation",
                table: "TranscriptSegments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);
        }
    }
}
