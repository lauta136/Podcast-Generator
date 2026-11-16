using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PodcastGenerator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Vocabulary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VocabularyItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    SegmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Text = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BaseForm = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Translation = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    TranslationLanguage = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Context = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Box = table.Column<int>(type: "integer", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VocabularyItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VocabularyItems_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_VocabularyItems_TranscriptSegments_SegmentId",
                        column: x => x.SegmentId,
                        principalTable: "TranscriptSegments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_VocabularyItems_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VocabularyItems_EpisodeId",
                table: "VocabularyItems",
                column: "EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_VocabularyItems_SegmentId",
                table: "VocabularyItems",
                column: "SegmentId");

            migrationBuilder.CreateIndex(
                name: "IX_VocabularyItems_UserId_CreatedAt",
                table: "VocabularyItems",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VocabularyItems_UserId_DueAt",
                table: "VocabularyItems",
                columns: new[] { "UserId", "DueAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VocabularyItems");
        }
    }
}
