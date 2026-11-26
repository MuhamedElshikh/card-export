using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdCardReader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBackCardFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpiryDate",
                table: "IdCards",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MaritalStatus",
                table: "IdCards",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Occupation",
                table: "IdCards",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpiryDate",
                table: "IdCards");

            migrationBuilder.DropColumn(
                name: "MaritalStatus",
                table: "IdCards");

            migrationBuilder.DropColumn(
                name: "Occupation",
                table: "IdCards");
        }
    }
}
