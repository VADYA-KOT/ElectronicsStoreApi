using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ElectronicsStoreApi.Migrations
{
    public partial class AddProductSpecifications : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Таблица справочника характеристик категорий.
            migrationBuilder.CreateTable(
                name: "category_specifications",
                columns: table => new
                {
                    id = table.Column<int>(
                        type: "integer",
                        nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),

                    category_id = table.Column<int>(
                        type: "integer",
                        nullable: false),

                    code = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false),

                    name = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false),

                    unit = table.Column<string>(
                        type: "character varying(30)",
                        maxLength: 30,
                        nullable: true),

                    sort_order = table.Column<int>(
                        type: "integer",
                        nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_category_specifications",
                        x => x.id);

                    table.CheckConstraint(
                        "CK_category_specifications_sort_order_nonnegative",
                        "sort_order >= 0");

                    table.ForeignKey(
                        name: "FK_category_specifications_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Таблица значений характеристик товаров.
            migrationBuilder.CreateTable(
                name: "product_specifications",
                columns: table => new
                {
                    id = table.Column<int>(
                        type: "integer",
                        nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),

                    product_id = table.Column<int>(
                        type: "integer",
                        nullable: false),

                    category_specification_id = table.Column<int>(
                        type: "integer",
                        nullable: false),

                    value = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_product_specifications",
                        x => x.id);

                    table.ForeignKey(
                        name: "FK_product_specifications_category_specifications_category_spe~",
                        column: x => x.category_specification_id,
                        principalTable: "category_specifications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);

                    table.ForeignKey(
                        name: "FK_product_specifications_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Уникальность кода характеристики внутри категории.
            migrationBuilder.CreateIndex(
                name: "IX_category_specifications_category_id_code",
                table: "category_specifications",
                columns: new[] { "category_id", "code" },
                unique: true);

            // Индекс для связи со справочником характеристик.
            migrationBuilder.CreateIndex(
                name: "IX_product_specifications_category_specification_id",
                table: "product_specifications",
                column: "category_specification_id");

            // У одного товара не может быть двух одинаковых характеристик.
            migrationBuilder.CreateIndex(
                name: "IX_product_specifications_product_id_category_specification_id",
                table: "product_specifications",
                columns: new[]
                {
                    "product_id",
                    "category_specification_id"
                },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Удаляем значения характеристик товаров.
            migrationBuilder.DropTable(
                name: "product_specifications");

            // Удаляем справочник характеристик.
            migrationBuilder.DropTable(
                name: "category_specifications");
        }
    }
}