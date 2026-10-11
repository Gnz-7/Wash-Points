using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WashPoints.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Slice02Comprobantes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "comprobantes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    turno_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    token_cifrado = table.Column<string>(type: "text", nullable: false),
                    expira_en = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    usado_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    validado_offline = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_comprobantes", x => x.id);
                    table.ForeignKey(
                        name: "FK_comprobantes_turnos_turno_id",
                        column: x => x.turno_id,
                        principalTable: "turnos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_comprobantes_hash",
                table: "comprobantes",
                column: "hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_comprobantes_turno_id",
                table: "comprobantes",
                column: "turno_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "comprobantes");
        }
    }
}
