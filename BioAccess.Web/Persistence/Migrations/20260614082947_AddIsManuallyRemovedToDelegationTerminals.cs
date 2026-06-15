using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BioAccess.Web.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIsManuallyRemovedToDelegationTerminals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsManuallyRemoved",
                table: "DelegationTerminals",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsManuallyRemoved",
                table: "DelegationTerminals");
        }
    }
}
