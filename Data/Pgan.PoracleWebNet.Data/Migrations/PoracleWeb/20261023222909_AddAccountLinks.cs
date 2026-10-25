using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace Pgan.PoracleWebNet.Data.Migrations.PoracleWeb
{
    /// <inheritdoc />
    public partial class AddAccountLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_links",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    discord_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    telegram_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    default_destination = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    discord_verified_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    telegram_verified_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_links", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_account_links_discord_id",
                table: "account_links",
                column: "discord_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_links_telegram_id",
                table: "account_links",
                column: "telegram_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_links");
        }
    }
}
