using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElectronicsStoreApi.Migrations
{
    /// <inheritdoc />
    public partial class AddProductPriceIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_products_price",
                table: "products",
                column: "price");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_products_price",
                table: "products");
        }
    }
}
