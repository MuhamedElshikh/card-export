using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdCardReader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IdCards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NationalId = table.Column<string>(type: "varchar(14)", unicode: false, maxLength: 14, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    BirthDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsMale = table.Column<bool>(type: "bit", nullable: false),
                    GovernorateCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    GovernorateEnglish = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    GovernorateArabic = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LowConfidence = table.Column<bool>(type: "bit", nullable: false),
                    ImageReference = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdCards", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IdCards_NationalId",
                table: "IdCards",
                column: "NationalId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdCards");
        }
    }
}
