using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cart.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CartItemPosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Position",
                table: "CartItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Itens que já existiam ganham posições 1..N por carrinho (a ordem original não foi guardada).
            migrationBuilder.Sql(
                """
                WITH numbered AS (
                    SELECT Position, ROW_NUMBER() OVER (PARTITION BY CartId ORDER BY ProductId) AS rn FROM CartItems
                )
                UPDATE numbered SET Position = rn;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Position",
                table: "CartItems");
        }
    }
}
