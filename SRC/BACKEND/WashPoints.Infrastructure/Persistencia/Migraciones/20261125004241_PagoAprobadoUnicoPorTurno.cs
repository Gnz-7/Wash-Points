using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WashPoints.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class PagoAprobadoUnicoPorTurno : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_pagos_turno_id",
                table: "pagos");

            migrationBuilder.CreateIndex(
                name: "IX_pagos_turno_id",
                table: "pagos",
                column: "turno_id",
                unique: true,
                filter: "\"resultado\" = 'approved'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_pagos_turno_id",
                table: "pagos");

            migrationBuilder.CreateIndex(
                name: "IX_pagos_turno_id",
                table: "pagos",
                column: "turno_id",
                unique: true);
        }
    }
}
