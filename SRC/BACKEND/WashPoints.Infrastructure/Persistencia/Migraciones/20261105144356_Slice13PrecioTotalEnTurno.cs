using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WashPoints.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Slice13PrecioTotalEnTurno : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Se agrega nullable: los turnos ya existentes todavía no tienen
            //    snapshot del precio.
            migrationBuilder.AddColumn<decimal>(
                name: "precio_total",
                table: "turnos",
                type: "numeric(10,2)",
                nullable: true);

            // 2) Backfill con fallback explícito (SPECS/slice-13 D-13.5 y §5.4).
            //    Orden de preferencia: precio del tipo -> seña efectivamente
            //    cobrada -> 0. El COALESCE evita que un turno huérfano, un dato
            //    de prueba o un tipo_lavado_id inconsistente dejen NULL y hagan
            //    fallar el SET NOT NULL de la base.
            migrationBuilder.Sql(
                """
                UPDATE turnos SET precio_total = COALESCE(
                    (SELECT precio_total FROM tipos_lavado WHERE id = turnos.tipo_lavado_id),
                    precio_sena,
                    0
                );
                """);

            // 3) Recién con la columna poblada se puede exigir NOT NULL.
            migrationBuilder.AlterColumn<decimal>(
                name: "precio_total",
                table: "turnos",
                type: "numeric(10,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "precio_total",
                table: "turnos");
        }
    }
}
