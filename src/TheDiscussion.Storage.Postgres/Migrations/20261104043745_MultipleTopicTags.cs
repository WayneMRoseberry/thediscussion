using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDiscussion.Storage.Postgres.Migrations
{
    /// <summary>
    /// A discussion has 1 to 5 topic tags instead of one. Each existing discussion's <c>topic_tag</c> is copied into
    /// <c>discussion_topic_tags</c> as its only tag (position 0) before the old column is dropped, so no data is lost.
    /// </summary>
    public partial class MultipleTopicTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "discussion_topic_tags",
                columns: table => new
                {
                    discussion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag = table.Column<string>(type: "text", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_discussion_topic_tags", x => new { x.discussion_id, x.tag });
                    table.ForeignKey(
                        name: "FK_discussion_topic_tags_discussions_discussion_id",
                        column: x => x.discussion_id,
                        principalTable: "discussions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO discussion_topic_tags (discussion_id, tag, position)
                SELECT id, topic_tag, 0 FROM discussions;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_discussion_topic_tags_tag",
                table: "discussion_topic_tags",
                column: "tag")
                .Annotation("Npgsql:IndexOperators", new[] { "text_pattern_ops" });

            migrationBuilder.DropColumn(
                name: "topic_tag",
                table: "discussions");
        }

        /// <summary>Restores the single tag column, keeping each discussion's first tag.</summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "topic_tag",
                table: "discussions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                UPDATE discussions AS d
                SET topic_tag = t.tag
                FROM discussion_topic_tags AS t
                WHERE t.discussion_id = d.id
                  AND t.position = (SELECT min(position) FROM discussion_topic_tags WHERE discussion_id = d.id);
                """);

            migrationBuilder.DropTable(
                name: "discussion_topic_tags");
        }
    }
}
