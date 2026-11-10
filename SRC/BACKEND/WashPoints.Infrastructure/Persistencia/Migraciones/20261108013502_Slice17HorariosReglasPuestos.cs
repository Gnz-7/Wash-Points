using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WashPoints.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Slice17HorariosReglasPuestos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "horarios_atencion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lavadero_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dia_semana = table.Column<int>(type: "integer", nullable: false),
                    apertura = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    cierre = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_horarios_atencion", x => x.id);
                    table.CheckConstraint("ck_horarios_atencion_dia_semana", "dia_semana BETWEEN 1 AND 7");
                    table.CheckConstraint("ck_horarios_atencion_rango", "apertura < cierre");
                });

            migrationBuilder.CreateTable(
                name: "puestos_tipos_lavado",
                columns: table => new
                {
                    puesto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_lavado_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_puestos_tipos_lavado", x => new { x.puesto_id, x.tipo_lavado_id });
                    table.ForeignKey(
                        name: "FK_puestos_tipos_lavado_puestos_puesto_id",
                        column: x => x.puesto_id,
                        principalTable: "puestos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_puestos_tipos_lavado_tipos_lavado_tipo_lavado_id",
                        column: x => x.tipo_lavado_id,
                        principalTable: "tipos_lavado",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_horarios_atencion_franja",
                table: "horarios_atencion",
                columns: new[] { "lavadero_id", "dia_semana", "apertura" },
                unique: true,
                filter: "\"activo\"");

            migrationBuilder.CreateIndex(
                name: "ix_horarios_atencion_lavadero_dia",
                table: "horarios_atencion",
                columns: new[] { "lavadero_id", "dia_semana" },
                filter: "\"activo\"");

            migrationBuilder.CreateIndex(
                name: "IX_puestos_tipos_lavado_tipo_lavado_id",
                table: "puestos_tipos_lavado",
                column: "tipo_lavado_id");

            // Backfill (slice-17, D-17.2): todo lavadero existente queda con la
            // semana por defecto lun–sáb 08:00–20:00. Los literales son fijos a
            // propósito: una migración no debe leer configuración de la app.
            migrationBuilder.Sql(
                """
                INSERT INTO horarios_atencion
                    (id, lavadero_id, dia_semana, apertura, cierre, activo, creado_en)
                SELECT gen_random_uuid(), l.id, d.dia,
                       TIME '08:00', TIME '20:00', TRUE, now()
                FROM lavaderos l
                CROSS JOIN generate_series(1, 6) AS d(dia);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "horarios_atencion");

            migrationBuilder.DropTable(
                name: "puestos_tipos_lavado");
        }
    }
}
