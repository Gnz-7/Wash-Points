using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WashPoints.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Slice15ReembolsosEjecucion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "intentos",
                table: "reembolsos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "mp_refund_id",
                table: "reembolsos",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "proximo_intento_en",
                table: "reembolsos",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "reclamado_en",
                table: "reembolsos",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ultimo_error",
                table: "reembolsos",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_reembolsos_mp_refund_id",
                table: "reembolsos",
                column: "mp_refund_id",
                unique: true,
                filter: "mp_refund_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_reembolsos_proximo_intento_en",
                table: "reembolsos",
                column: "proximo_intento_en",
                filter: "estado = 'pendiente'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_reembolsos_mp_refund_id",
                table: "reembolsos");

            migrationBuilder.DropIndex(
                name: "IX_reembolsos_proximo_intento_en",
                table: "reembolsos");

            migrationBuilder.DropColumn(
                name: "intentos",
                table: "reembolsos");

            migrationBuilder.DropColumn(
                name: "mp_refund_id",
                table: "reembolsos");

            migrationBuilder.DropColumn(
                name: "proximo_intento_en",
                table: "reembolsos");

            migrationBuilder.DropColumn(
                name: "reclamado_en",
                table: "reembolsos");

            migrationBuilder.DropColumn(
                name: "ultimo_error",
                table: "reembolsos");
        }
    }
}
