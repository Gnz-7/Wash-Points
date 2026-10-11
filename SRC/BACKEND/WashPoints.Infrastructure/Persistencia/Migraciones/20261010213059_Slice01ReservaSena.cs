using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WashPoints.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Slice01ReservaSena : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Slice-01: el pendiente_pago VIGENTE pasa a bloquear el intervalo.
            // La Inicial creó la constraint con SQL puro (EF no la modela), así
            // que el cambio de predicado también va en SQL puro (SPECS/slice-01
            // T-11). Primero se suelta, después de los cambios se re-crea con el
            // predicado de 3 estados.
            migrationBuilder.Sql("""
                ALTER TABLE turnos
                    DROP CONSTRAINT turnos_no_solapamiento;
                """);

            migrationBuilder.AddColumn<DateTime>(
                name: "expira_en",
                table: "turnos",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "turnos",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pagos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    turno_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mp_payment_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    tipo_evento = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    resultado = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    procesado_en = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pagos", x => x.id);
                    table.ForeignKey(
                        name: "FK_pagos_turnos_turno_id",
                        column: x => x.turno_id,
                        principalTable: "turnos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "turnos_idempotency_unicos",
                table: "turnos",
                column: "idempotency_key",
                unique: true,
                filter: "\"idempotency_key\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_pagos_mp_payment_id",
                table: "pagos",
                column: "mp_payment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pagos_turno_id",
                table: "pagos",
                column: "turno_id",
                unique: true);

            // Re-crea la exclusión con los 3 estados que bloquean. El índice
            // parcial no evalúa `expira_en` (los índices parciales no se
            // re-evalúan con el tiempo): la liberación la hace el worker al
            // transicionar a 'expirado' (SPECS/slice-01 §3).
            migrationBuilder.Sql("""
                ALTER TABLE turnos
                    ADD CONSTRAINT turnos_no_solapamiento
                    EXCLUDE USING gist (
                        puesto_id WITH =,
                        rango WITH &&
                    )
                    WHERE (estado IN ('confirmado', 'en_curso', 'pendiente_pago'))
                    DEFERRABLE INITIALLY IMMEDIATE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pagos");

            migrationBuilder.DropIndex(
                name: "turnos_idempotency_unicos",
                table: "turnos");

            migrationBuilder.DropColumn(
                name: "expira_en",
                table: "turnos");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "turnos");
        }
    }
}
