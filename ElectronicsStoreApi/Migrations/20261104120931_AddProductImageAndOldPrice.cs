using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElectronicsStoreApi.Migrations
{
    /// <inheritdoc />
    public partial class AddProductImageAndOldPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "image_url",
                table: "products",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "old_price",
                table: "products",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_products_old_price_greater_than_price",
                table: "products",
                sql: "old_price IS NULL OR old_price > price");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_products_old_price_greater_than_price",
                table: "products");

            migrationBuilder.DropColumn(
                name: "image_url",
                table: "products");

            migrationBuilder.DropColumn(
                name: "old_price",
                table: "products");
        }
    }
}
