using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace Pgan.PoracleWebNet.Data.Migrations.PoracleWeb
{
    /// <inheritdoc />
    public partial class AddOidcSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "oidc_sessions",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    session_token_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    family_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                    family_issued_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    user_id = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    encrypted_refresh_token = table.Column<string>(type: "longtext", nullable: false),
                    expires_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    revoked_reason = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    replaced_by_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    ip_address = table.Column<string>(type: "varchar(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_oidc_sessions", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_oidc_sessions_family_id",
                table: "oidc_sessions",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "IX_oidc_sessions_revoked_at_expires_at",
                table: "oidc_sessions",
                columns: new[] { "revoked_at", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_oidc_sessions_session_token_hash",
                table: "oidc_sessions",
                column: "session_token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_oidc_sessions_user_id_revoked_at",
                table: "oidc_sessions",
                columns: new[] { "user_id", "revoked_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "oidc_sessions");
        }
    }
}
