using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WashPoints.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarUbicacionLavaderos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "direccion",
                table: "lavaderos",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "latitud",
                table: "lavaderos",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "longitud",
                table: "lavaderos",
                type: "double precision",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_lavaderos_ubicacion",
                table: "lavaderos",
                columns: new[] { "latitud", "longitud" },
                filter: "\"latitud\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_lavaderos_latitud",
                table: "lavaderos",
                sql: "latitud IS NULL OR (latitud >= -90 AND latitud <= 90)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_lavaderos_longitud",
                table: "lavaderos",
                sql: "longitud IS NULL OR (longitud >= -180 AND longitud <= 180)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_lavaderos_ubicacion_completa",
                table: "lavaderos",
                sql: "(latitud IS NULL) = (longitud IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_lavaderos_ubicacion",
                table: "lavaderos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_lavaderos_latitud",
                table: "lavaderos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_lavaderos_longitud",
                table: "lavaderos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_lavaderos_ubicacion_completa",
                table: "lavaderos");

            migrationBuilder.DropColumn(
                name: "direccion",
                table: "lavaderos");

            migrationBuilder.DropColumn(
                name: "latitud",
                table: "lavaderos");

            migrationBuilder.DropColumn(
                name: "longitud",
                table: "lavaderos");
        }
    }
}
