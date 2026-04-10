using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HelpDesk.Models.Migrations
{
    /// <inheritdoc />
    public partial class AddPageSeparators : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PageType",
                table: "Pages",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SeparatorText",
                table: "Pages",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Pages_Separator_NoNavigation",
                table: "Pages",
                sql: "([PageType] = 0) OR ([Route] IS NULL AND [Url] IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Pages_Separator_NoNavigation",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "PageType",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "SeparatorText",
                table: "Pages");
        }
    }
}
