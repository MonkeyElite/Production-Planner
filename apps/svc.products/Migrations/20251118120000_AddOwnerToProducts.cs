using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace svc.products.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerToProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "Products",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.DropIndex(
                name: "IX_Products_Name",
                table: "Products");

            migrationBuilder.CreateIndex(
                name: "IX_Products_OwnerId_Name",
                table: "Products",
                columns: new[] { "OwnerId", "Name" },
                unique: true);

            migrationBuilder.Sql("UPDATE \"Products\" SET \"OwnerId\" = '2b6a180e-5c5f-41cb-b807-54cf72d23b38' WHERE \"OwnerId\" = '00000000-0000-0000-0000-000000000000';");

            migrationBuilder.CreateTable(
                name: "ProductionLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CapacityPerShift = table.Column<int>(type: "integer", nullable: false),
                    ShiftSchedule = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionLines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductionLineProducts",
                columns: table => new
                {
                    ProductionLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionLineProducts", x => new { x.ProductionLineId, x.ProductId });
                    table.ForeignKey(
                        name: "FK_ProductionLineProducts_ProductionLines_ProductionLineId",
                        column: x => x.ProductionLineId,
                        principalTable: "ProductionLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductionLineProducts_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionLineProducts_ProductId",
                table: "ProductionLineProducts",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionLines_OwnerId_Name",
                table: "ProductionLines",
                columns: new[] { "OwnerId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductionLineProducts");

            migrationBuilder.DropTable(
                name: "ProductionLines");

            migrationBuilder.DropIndex(
                name: "IX_Products_OwnerId_Name",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Products");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Name",
                table: "Products",
                column: "Name",
                unique: true);
        }
    }
}
