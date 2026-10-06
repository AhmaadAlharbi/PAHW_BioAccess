using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Terminals.Web.Persistence.Migrations
{
    /// <inheritdoc />
    [Migration("20261006092305_AddAttendanceRequests")]
    public partial class AddAttendanceRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AttendanceRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    EmployeeName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AttendanceDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByEmployeeId = table.Column<int>(type: "int", nullable: false),
                    CreatedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    HrNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "PendingIT"),
                    ITNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AnsweredByEmployeeId = table.Column<int>(type: "int", nullable: true),
                    AnsweredByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AnsweredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceRequestAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AttendanceRequestId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceRequestAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceRequestAttachments_AttendanceRequests_AttendanceRequestId",
                        column: x => x.AttendanceRequestId,
                        principalTable: "AttendanceRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceRequestItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AttendanceRequestId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Reply = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceRequestItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceRequestItems_AttendanceRequests_AttendanceRequestId",
                        column: x => x.AttendanceRequestId,
                        principalTable: "AttendanceRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRequestAttachments_AttendanceRequestId",
                table: "AttendanceRequestAttachments",
                column: "AttendanceRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRequestItems_AttendanceRequestId",
                table: "AttendanceRequestItems",
                column: "AttendanceRequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceRequestAttachments");

            migrationBuilder.DropTable(
                name: "AttendanceRequestItems");

            migrationBuilder.DropTable(
                name: "AttendanceRequests");
        }
    }
}
