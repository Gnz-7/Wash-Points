using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WashPoints.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Slice20SuscripcionVinculacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "mp_access_token",
                table: "lavaderos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mp_refresh_token",
                table: "lavaderos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mp_user_id",
                table: "lavaderos",
                type: "varchar",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "mp_vinculado",
                table: "lavaderos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "mp_vinculado_el",
                table: "lavaderos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "suscripcion_activa",
                table: "lavaderos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "suscripcion_vence_el",
                table: "lavaderos",
                type: "timestamp with time zone",
                nullable: true);

            // Backfill (SPECS/slice-20, D-20.8): los lavaderos existentes
            // quedan con la suscripción activa para que el despliegue no los
            // interrumpa. El alta de ahora en adelante nace pendiente; este
            // UPDATE sólo aplica a las filas previas al slice.
            migrationBuilder.Sql("UPDATE lavaderos SET suscripcion_activa = true;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "mp_access_token",
                table: "lavaderos");

            migrationBuilder.DropColumn(
                name: "mp_refresh_token",
                table: "lavaderos");

            migrationBuilder.DropColumn(
                name: "mp_user_id",
                table: "lavaderos");

            migrationBuilder.DropColumn(
                name: "mp_vinculado",
                table: "lavaderos");

            migrationBuilder.DropColumn(
                name: "mp_vinculado_el",
                table: "lavaderos");

            migrationBuilder.DropColumn(
                name: "suscripcion_activa",
                table: "lavaderos");

            migrationBuilder.DropColumn(
                name: "suscripcion_vence_el",
                table: "lavaderos");
        }
    }
}
