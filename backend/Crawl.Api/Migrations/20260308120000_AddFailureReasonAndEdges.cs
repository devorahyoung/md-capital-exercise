using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crawl.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFailureReasonAndEdges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add optional FailureReason column to Jobs
            migrationBuilder.AddColumn<string>(
                name: "FailureReason",
                table: "Jobs",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            // Create Edges table (parent → child directed links discovered per job)
            migrationBuilder.CreateTable(
                name: "Edges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ChildUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Edges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Edges_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Edges_JobId",
                table: "Edges",
                column: "JobId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Edges");

            migrationBuilder.DropColumn(
                name: "FailureReason",
                table: "Jobs");
        }
    }
}
