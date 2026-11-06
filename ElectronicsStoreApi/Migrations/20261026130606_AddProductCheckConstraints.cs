using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ElectronicsStoreApi.Migrations
{
    public partial class AddProductCheckConstraints : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE public.products
                    DROP CONSTRAINT IF EXISTS
                    "CK_products_price_positive";

                ALTER TABLE public.products
                    DROP CONSTRAINT IF EXISTS
                    "CK_products_quantity_nonnegative";
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_products_price_positive",
                table: "products",
                sql: "price > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_products_quantity_nonnegative",
                table: "products",
                sql: "quantity >= 0");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_products_price_positive",
                table: "products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_products_quantity_nonnegative",
                table: "products");
        }
    }
}