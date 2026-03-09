using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crawl.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPageOutgoingLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "OutgoingLinks",
                table: "Pages",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OutgoingLinks",
                table: "Pages");
        }
    }
}
