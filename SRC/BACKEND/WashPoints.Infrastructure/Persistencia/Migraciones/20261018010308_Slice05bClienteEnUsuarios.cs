using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WashPoints.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Slice05bClienteEnUsuarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "cliente_id",
                table: "usuarios",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_usuarios_cliente_id",
                table: "usuarios",
                column: "cliente_id");

            migrationBuilder.AddCheckConstraint(
                name: "usuarios_admin_sin_cliente",
                table: "usuarios",
                sql: "rol <> 'administrador' OR cliente_id IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_usuarios_clientes_cliente_id",
                table: "usuarios",
                column: "cliente_id",
                principalTable: "clientes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_usuarios_clientes_cliente_id",
                table: "usuarios");

            migrationBuilder.DropIndex(
                name: "IX_usuarios_cliente_id",
                table: "usuarios");

            migrationBuilder.DropCheckConstraint(
                name: "usuarios_admin_sin_cliente",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "cliente_id",
                table: "usuarios");
        }
    }
}
