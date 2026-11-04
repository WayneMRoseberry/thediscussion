using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDiscussion.Storage.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class StatementEditsAndSubtreeDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_statements_statements_parent_id",
                table: "statements");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "edited_at",
                table: "statements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_statements_statements_parent_id",
                table: "statements",
                column: "parent_id",
                principalTable: "statements",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_statements_statements_parent_id",
                table: "statements");

            migrationBuilder.DropColumn(
                name: "edited_at",
                table: "statements");

            migrationBuilder.AddForeignKey(
                name: "FK_statements_statements_parent_id",
                table: "statements",
                column: "parent_id",
                principalTable: "statements",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
