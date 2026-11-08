using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PodcastGenerator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TtsUsage",
                columns: table => new
                {
                    ModelFamily = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    Characters = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TtsUsage", x => new { x.ModelFamily, x.Month });
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Seasons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Topic = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Level = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ArcJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seasons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Seasons_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserPreferences",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    NativeLanguage = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    TargetLanguage = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    DefaultLevel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DefaultDurationMin = table.Column<int>(type: "integer", nullable: false),
                    Interests = table.Column<List<string>>(type: "text[]", nullable: false),
                    VoiceName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    VoiceModelFamily = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PlaybackSpeed = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPreferences", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserPreferences_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Episodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonId = table.Column<Guid>(type: "uuid", nullable: true),
                    SeasonOrder = table.Column<int>(type: "integer", nullable: true),
                    Topic = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    LanguageCode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Level = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Style = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DurationMin = table.Column<int>(type: "integer", nullable: false),
                    TargetWords = table.Column<int>(type: "integer", nullable: false),
                    VoiceName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    VoiceModelFamily = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    DifficultyRating = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AudioStorageKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    AudioDurationSec = table.Column<double>(type: "double precision", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Episodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Episodes_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Episodes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EpisodeSections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    TargetWords = table.Column<int>(type: "integer", nullable: false),
                    Focus = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    WordCount = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EpisodeSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EpisodeSections_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GenerationJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LockedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GenerationJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GenerationJobs_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ListeningProgress",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    PositionSec = table.Column<double>(type: "double precision", nullable: false),
                    Completed = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningProgress", x => new { x.UserId, x.EpisodeId });
                    table.ForeignKey(
                        name: "FK_ListeningProgress_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ListeningProgress_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AudioSegments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParagraphIndex = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    CacheKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    VoiceName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ModelFamily = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Characters = table.Column<int>(type: "integer", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    DurationSec = table.Column<double>(type: "double precision", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AudioSegments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AudioSegments_EpisodeSections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "EpisodeSections",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AudioSegments_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TranscriptSegments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    ParagraphIndex = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    Translation = table.Column<string>(type: "text", nullable: true),
                    StartSec = table.Column<double>(type: "double precision", nullable: true),
                    EndSec = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TranscriptSegments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TranscriptSegments_EpisodeSections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "EpisodeSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "CreatedAt", "DisplayName" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000001"), new DateTimeOffset(new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Local learner" });

            migrationBuilder.CreateIndex(
                name: "IX_AudioSegments_CacheKey",
                table: "AudioSegments",
                column: "CacheKey");

            migrationBuilder.CreateIndex(
                name: "IX_AudioSegments_EpisodeId_Order",
                table: "AudioSegments",
                columns: new[] { "EpisodeId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AudioSegments_SectionId",
                table: "AudioSegments",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_SeasonId_SeasonOrder",
                table: "Episodes",
                columns: new[] { "SeasonId", "SeasonOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_Title",
                table: "Episodes",
                column: "Title");

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_Topic",
                table: "Episodes",
                column: "Topic");

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_UserId_CreatedAt",
                table: "Episodes",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_UserId_LanguageCode",
                table: "Episodes",
                columns: new[] { "UserId", "LanguageCode" });

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_UserId_Level",
                table: "Episodes",
                columns: new[] { "UserId", "Level" });

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_UserId_Status",
                table: "Episodes",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EpisodeSections_EpisodeId_Order",
                table: "EpisodeSections",
                columns: new[] { "EpisodeId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GenerationJobs_EpisodeId",
                table: "GenerationJobs",
                column: "EpisodeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GenerationJobs_Status_LockedUntil",
                table: "GenerationJobs",
                columns: new[] { "Status", "LockedUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningProgress_EpisodeId",
                table: "ListeningProgress",
                column: "EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_UserId",
                table: "Seasons",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TranscriptSegments_SectionId_Order",
                table: "TranscriptSegments",
                columns: new[] { "SectionId", "Order" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AudioSegments");

            migrationBuilder.DropTable(
                name: "GenerationJobs");

            migrationBuilder.DropTable(
                name: "ListeningProgress");

            migrationBuilder.DropTable(
                name: "TranscriptSegments");

            migrationBuilder.DropTable(
                name: "TtsUsage");

            migrationBuilder.DropTable(
                name: "UserPreferences");

            migrationBuilder.DropTable(
                name: "EpisodeSections");

            migrationBuilder.DropTable(
                name: "Episodes");

            migrationBuilder.DropTable(
                name: "Seasons");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
