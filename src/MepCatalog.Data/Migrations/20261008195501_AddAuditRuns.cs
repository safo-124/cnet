using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MepCatalog.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectGlobalId = table.Column<string>(type: "TEXT", maxLength: 22, nullable: false),
                    ProjectName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    AuditedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Total = table.Column<int>(type: "INTEGER", nullable: false),
                    Ok = table.Column<int>(type: "INTEGER", nullable: false),
                    NeedsUpdate = table.Column<int>(type: "INTEGER", nullable: false),
                    Unidentified = table.Column<int>(type: "INTEGER", nullable: false),
                    NotInCatalog = table.Column<int>(type: "INTEGER", nullable: false),
                    CategoryMismatch = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditRuns_ProjectGlobalId_AuditedUtc",
                table: "AuditRuns",
                columns: new[] { "ProjectGlobalId", "AuditedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditRuns");
        }
    }
}
