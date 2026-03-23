using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace Pgan.PoracleWebNet.Data.Migrations.PoracleWeb
{
    /// <inheritdoc />
    public partial class InitialPoracleWeb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "quick_pick_applied_states",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    user_id = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    profile_no = table.Column<int>(type: "int", nullable: false),
                    quick_pick_id = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    applied_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    exclude_pokemon_ids_json = table.Column<string>(type: "json", nullable: true),
                    tracked_uids_json = table.Column<string>(type: "json", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quick_pick_applied_states", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "quick_pick_definitions",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    icon = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, defaultValue: "bolt"),
                    category = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, defaultValue: "Common"),
                    alarm_type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, defaultValue: "monster"),
                    sort_order = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    enabled = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    scope = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false, defaultValue: "global"),
                    owner_user_id = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    filters_json = table.Column<string>(type: "json", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quick_pick_definitions", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "site_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    category = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    key = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "text", nullable: true),
                    value_type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, defaultValue: "string")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_site_settings", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "user_geofences",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    human_id = table.Column<string>(type: "longtext", nullable: false),
                    koji_name = table.Column<string>(type: "longtext", nullable: false),
                    display_name = table.Column<string>(type: "longtext", nullable: false),
                    group_name = table.Column<string>(type: "longtext", nullable: false),
                    parent_id = table.Column<int>(type: "int", nullable: false),
                    polygon_json = table.Column<string>(type: "longtext", nullable: true),
                    status = table.Column<string>(type: "longtext", nullable: false),
                    submitted_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    reviewed_by = table.Column<string>(type: "longtext", nullable: true),
                    reviewed_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    review_notes = table.Column<string>(type: "longtext", nullable: true),
                    promoted_name = table.Column<string>(type: "longtext", nullable: true),
                    discord_thread_id = table.Column<string>(type: "longtext", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_geofences", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "webhook_delegates",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    webhook_id = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    user_id = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_delegates", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_quick_pick_applied_states_user_id_profile_no_quick_pick_id",
                table: "quick_pick_applied_states",
                columns: new[] { "user_id", "profile_no", "quick_pick_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_site_settings_key",
                table: "site_settings",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_webhook_delegates_webhook_id_user_id",
                table: "webhook_delegates",
                columns: new[] { "webhook_id", "user_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quick_pick_applied_states");

            migrationBuilder.DropTable(
                name: "quick_pick_definitions");

            migrationBuilder.DropTable(
                name: "site_settings");

            migrationBuilder.DropTable(
                name: "user_geofences");

            migrationBuilder.DropTable(
                name: "webhook_delegates");
        }
    }
}
