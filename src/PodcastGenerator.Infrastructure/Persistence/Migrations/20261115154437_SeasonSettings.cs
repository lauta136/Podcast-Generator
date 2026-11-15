using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PodcastGenerator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeasonSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Seasons",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationMin",
                table: "Seasons",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EpisodeCount",
                table: "Seasons",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "SpeakingRate",
                table: "Seasons",
                type: "numeric(3,2)",
                precision: 3,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Style",
                table: "Seasons",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "Seasons",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "Seasons");

            migrationBuilder.DropColumn(
                name: "DurationMin",
                table: "Seasons");

            migrationBuilder.DropColumn(
                name: "EpisodeCount",
                table: "Seasons");

            migrationBuilder.DropColumn(
                name: "SpeakingRate",
                table: "Seasons");

            migrationBuilder.DropColumn(
                name: "Style",
                table: "Seasons");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Seasons");
        }
    }
}
