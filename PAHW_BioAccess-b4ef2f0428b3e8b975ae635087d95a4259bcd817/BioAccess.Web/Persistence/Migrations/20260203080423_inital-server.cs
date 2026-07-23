using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Terminals.Web.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class initalserver : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AllowedUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Department = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ValidUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsAdmin = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllowedUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Delegations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActivatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiredAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Delegations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Regions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Regions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DelegationTerminals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DelegationId = table.Column<int>(type: "int", nullable: false),
                    TerminalId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    WasAssignedBefore = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelegationTerminals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DelegationTerminals_Delegations_DelegationId",
                        column: x => x.DelegationId,
                        principalTable: "Delegations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TerminalRegionMaps",
                columns: table => new
                {
                    TerminalId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RegionId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerminalRegionMaps", x => x.TerminalId);
                    table.ForeignKey(
                        name: "FK_TerminalRegionMaps_Regions_RegionId",
                        column: x => x.RegionId,
                        principalTable: "Regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "AllowedUsers",
                columns: new[] { "Id", "Department", "Email", "EmployeeId", "FullName", "IsActive", "IsAdmin", "ValidUntil" },
                values: new object[] { 1, "", "admin@admin.com", 7300, "Ø£Ø­Ù…Ø¯ Ø²ÙŠØ¯ Ø§Ù„Ø­Ø±Ø¨ÙŠ", true, true, null });

            migrationBuilder.InsertData(
                table: "Regions",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 1, "Ø§Ù„Ù…Ø¨Ù†Ù‰ Ø§Ù„Ø±Ø¦ÙŠØ³ÙŠ" },
                    { 2, "Ø§Ù„Ù…Ø·Ù„Ø§Ø¹" },
                    { 3, "Ø¨Ø±Ø¬ Ø§Ù„ØªØ­Ø±ÙŠØ±" },
                    { 4, "ØµØ¨Ø§Ø­ Ø§Ù„Ø³Ø§Ù„Ù…" },
                    { 5, "Ø§Ù„Ø¬Ù‡Ø±Ø§Ø¡ - Ø­ÙƒÙˆÙ…Ø© Ù…ÙˆÙ„" },
                    { 6, "Ø§Ù„Ø¬Ù‡Ø±Ø§Ø¡ - ØªÙŠÙ…Ø§Ø¡" },
                    { 7, "Ø¬Ø§Ø¨Ø± Ø§Ù„Ø£Ø­Ù…Ø¯" },
                    { 8, "Ø³Ø¹Ø¯ Ø§Ù„Ø¹Ø¨Ø¯Ø§Ù„Ù„Ù‡" },
                    { 9, "Ø§Ù„ØµÙ„ÙŠØ¨ÙŠØ©" },
                    { 10, "Ø§Ù„Ù‚Ø±ÙŠÙ† - Ø­ÙƒÙˆÙ…Ø© Ù…ÙˆÙ„" },
                    { 11, "Ù…Ø¨Ø§Ø±Ùƒ Ø§Ù„ÙƒØ¨ÙŠØ±" },
                    { 12, "Ø§Ù„Ù†Ù‡Ø¶Ø©" },
                    { 13, "ØºØ±Ø¨ Ø§Ù„Ø¬Ù„ÙŠØ¨" },
                    { 14, "Ù…ÙˆØ§Ù‚Ø¹ Ø£Ø®Ø±Ù‰" },
                    { 15, "Ø§Ù„Ø³Ø§Ù„Ù…ÙŠ" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AllowedUsers_EmployeeId",
                table: "AllowedUsers",
                column: "EmployeeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DelegationTerminals_DelegationId",
                table: "DelegationTerminals",
                column: "DelegationId");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalRegionMaps_RegionId",
                table: "TerminalRegionMaps",
                column: "RegionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AllowedUsers");

            migrationBuilder.DropTable(
                name: "DelegationTerminals");

            migrationBuilder.DropTable(
                name: "TerminalRegionMaps");

            migrationBuilder.DropTable(
                name: "Delegations");

            migrationBuilder.DropTable(
                name: "Regions");
        }
    }
}
