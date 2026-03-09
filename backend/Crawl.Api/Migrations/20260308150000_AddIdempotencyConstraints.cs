using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crawl.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Unique constraint: same URL must not be stored twice for the same job.
            // This is the DB-level guard against duplicate rows if a message is redelivered.
            migrationBuilder.CreateIndex(
                name: "UX_Pages_JobId_Url",
                table: "Pages",
                columns: new[] { "JobId", "Url" },
                unique: true);

            // Unique constraint: no duplicate parent→child edge per job.
            migrationBuilder.CreateIndex(
                name: "UX_Edges_JobId_ParentUrl_ChildUrl",
                table: "Edges",
                columns: new[] { "JobId", "ParentUrl", "ChildUrl" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "UX_Edges_JobId_ParentUrl_ChildUrl", table: "Edges");
            migrationBuilder.DropIndex(name: "UX_Pages_JobId_Url", table: "Pages");
        }
    }
}
