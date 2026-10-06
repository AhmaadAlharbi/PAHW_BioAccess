using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Terminals.Web.Persistence.Migrations
{
    /// <inheritdoc />
    [Migration("20261006000000_AddUserTypeToAllowedUsers")]
    public partial class AddUserTypeToAllowedUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserType",
                table: "AllowedUsers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Attendance");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UserType",
                table: "AllowedUsers");
        }
    }
}
