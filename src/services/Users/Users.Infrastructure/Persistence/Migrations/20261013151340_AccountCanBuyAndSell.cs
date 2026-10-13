using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Users.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountCanBuyAndSell : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ordem importa: cria as colunas novas, converte os dados e só então remove a antiga.
            // Contas 'Seller' viram compradoras + vendedoras desde a data de criação.
            migrationBuilder.AddColumn<bool>(
                name: "IsSeller",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SellerSince",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.Sql("UPDATE [Users] SET [IsSeller] = 1, [SellerSince] = [CreatedAt] WHERE [Role] = N'Seller';");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "Users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "Users",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Buyer");

            migrationBuilder.Sql("UPDATE [Users] SET [Role] = N'Seller' WHERE [IsSeller] = 1;");

            migrationBuilder.DropColumn(
                name: "IsSeller",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SellerSince",
                table: "Users");
        }

    }
}
