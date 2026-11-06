using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WashPoints.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Slice14ReembolsosYCanceladoPor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancelado_por",
                table: "turnos",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            // Backfill (SPECS/slice-14 §5.2): los turnos que ya estaban
            // cancelados son bajas administrativas, porque el cliente todavía no
            // podía cancelar desde la app. Sin esto quedarían sin autoría y el
            // histórico no podría distinguir quién los dio de baja.
            migrationBuilder.Sql(
                "UPDATE turnos SET cancelado_por = 'administrador' WHERE estado = 'cancelado';");

            migrationBuilder.CreateTable(
                name: "reembolsos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    turno_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    motivo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    monto = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    creado_en = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    procesado_en = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reembolsos", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reembolsos_estado_creado_en",
                table: "reembolsos",
                columns: new[] { "estado", "creado_en" });

            migrationBuilder.CreateIndex(
                name: "IX_reembolsos_turno_id",
                table: "reembolsos",
                column: "turno_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reembolsos");

            migrationBuilder.DropColumn(
                name: "cancelado_por",
                table: "turnos");
        }
    }
}
