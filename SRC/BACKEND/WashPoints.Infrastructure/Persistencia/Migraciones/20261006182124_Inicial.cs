using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace WashPoints.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "clientes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clientes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lavaderos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lavaderos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "puestos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lavadero_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_puestos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tipos_lavado",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lavadero_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    duracion_minutos = table.Column<int>(type: "integer", nullable: false),
                    precio_total = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    precio_sena = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tipos_lavado", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "turnos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lavadero_id = table.Column<Guid>(type: "uuid", nullable: false),
                    puesto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_lavado_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rango = table.Column<NpgsqlRange<DateTime>>(type: "tsrange", nullable: false),
                    estado = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    origen = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    precio_sena = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    mp_preference_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    creado_en = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    confirmado_en = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    cerrado_en = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_turnos", x => x.id);
                    table.ForeignKey(
                        name: "FK_turnos_puestos_puesto_id",
                        column: x => x.puesto_id,
                        principalTable: "puestos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_turnos_tipos_lavado_tipo_lavado_id",
                        column: x => x.tipo_lavado_id,
                        principalTable: "tipos_lavado",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_puestos_lavadero_id_nombre",
                table: "puestos",
                columns: new[] { "lavadero_id", "nombre" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tipos_lavado_lavadero_id_nombre",
                table: "tipos_lavado",
                columns: new[] { "lavadero_id", "nombre" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_turnos_lavadero_id_estado",
                table: "turnos",
                columns: new[] { "lavadero_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "IX_turnos_puesto_id",
                table: "turnos",
                column: "puesto_id");

            migrationBuilder.CreateIndex(
                name: "IX_turnos_tipo_lavado_id",
                table: "turnos",
                column: "tipo_lavado_id");

            // Restricción de no-solapamiento: la garantía de RB-2 vive en el
            // motor. Un puesto no puede tener dos turnos cuyos rangos se
            // solapen, salvo que ambos estén fuera del predicado (es decir,
            // ninguno ocupe el puesto). Ver ARCHITECTURE §4.1.
            //
            // EF Core no tiene API fluente para exclusion constraints: se
            // emite como SQL puro (SPECS/slice-00-nucleo-turnos.md T-04).
            migrationBuilder.Sql(@"
CREATE EXTENSION IF NOT EXISTS btree_gist;

ALTER TABLE turnos
    ADD CONSTRAINT turnos_no_solapamiento
    EXCLUDE USING gist (
        puesto_id WITH =,
        rango WITH &&
    )
    WHERE (estado IN ('confirmado', 'en_curso'));
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clientes");

            migrationBuilder.DropTable(
                name: "lavaderos");

            migrationBuilder.DropTable(
                name: "turnos");

            migrationBuilder.DropTable(
                name: "puestos");

            migrationBuilder.DropTable(
                name: "tipos_lavado");
        }
    }
}
