using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheDiscussion.Storage.Postgres.Migrations
{
    /// <summary>
    /// Support groups get a name (nested-named-groups AC-3). The new nullable <c>name</c> column is added without a
    /// default, so every existing row gets <c>NULL</c>: groups stored before this change stay unnamed and keep their
    /// <c>group_type</c>, and no other column or row changes. New groups are always named by the application tier.
    /// </summary>
    public partial class NamedSupportGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "statements",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "name",
                table: "statements");
        }
    }
}
