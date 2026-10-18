using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WashPoints.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Slice05Usuarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "usuarios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre_usuario = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    hash_contrasena = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    rol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    lavadero_id = table.Column<Guid>(type: "uuid", nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usuarios", x => x.id);
                    table.CheckConstraint("usuarios_admin_requiere_lavadero", "rol <> 'administrador' OR lavadero_id IS NOT NULL");
                    table.CheckConstraint("usuarios_rol_valido", "rol IN ('cliente', 'administrador')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_usuarios_nombre_usuario",
                table: "usuarios",
                column: "nombre_usuario",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "usuarios");
        }
    }
}
