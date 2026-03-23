using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pgan.PoracleWebNet.Data.Migrations.PoracleWeb
{
    /// <inheritdoc />
    public partial class AddAlarmTypeAndMissingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "alarm_type",
                table: "quick_pick_applied_states",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "monster");

            migrationBuilder.CreateIndex(
                name: "IX_webhook_delegates_user_id",
                table: "webhook_delegates",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_quick_pick_definitions_scope_owner_user_id",
                table: "quick_pick_definitions",
                columns: new[] { "scope", "owner_user_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_webhook_delegates_user_id",
                table: "webhook_delegates");

            migrationBuilder.DropIndex(
                name: "IX_quick_pick_definitions_scope_owner_user_id",
                table: "quick_pick_definitions");

            migrationBuilder.DropColumn(
                name: "alarm_type",
                table: "quick_pick_applied_states");
        }
    }
}
