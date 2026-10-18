using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Seguridad;

namespace WashPoints.Infrastructure.Persistencia.Configuraciones;

/// <summary>
/// Configuración EF Core de <see cref="Usuario"/> (tabla `usuarios`).
///
/// Sigue el patrón de <c>PagoConfig</c>: se aplica desde
/// <see cref="WashPointsDbContext.OnModelCreating"/>. Ver SPECS/slice-05 T-51
/// (D-05.1).
/// </summary>
public sealed class UsuarioConfig : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> usuario)
    {
        usuario.ToTable("usuarios", tabla =>
        {
            // D-05.1 (rol): solo se aceptan los roles conocidos.
            tabla.HasCheckConstraint(
                "usuarios_rol_valido",
                "rol IN ('cliente', 'administrador')");

            // D-05.1 (lavadero_id): un administrador siempre pertenece a un
            // lavadero; un cliente no tiene ninguno.
            tabla.HasCheckConstraint(
                "usuarios_admin_requiere_lavadero",
                "rol <> 'administrador' OR lavadero_id IS NOT NULL");

            // D-06.1 (cliente_id): el vínculo es del cliente de negocio; el
            // administrador se vincula al lavadero, nunca a un cliente.
            tabla.HasCheckConstraint(
                "usuarios_admin_sin_cliente",
                "rol <> 'administrador' OR cliente_id IS NULL");
        });

        usuario.HasKey(u => u.Id);

        usuario.Property(u => u.Id).HasColumnName("id");
        usuario.Property(u => u.NombreUsuario)
            .HasColumnName("nombre_usuario")
            .HasMaxLength(80)
            .IsRequired();
        usuario.Property(u => u.HashContrasena)
            .HasColumnName("hash_contrasena")
            .HasMaxLength(256)
            .IsRequired();
        usuario.Property(u => u.Rol)
            .HasColumnName("rol")
            .HasConversion(new ConvertidorEnumSnakeCase<RolUsuario>())
            .HasMaxLength(32)
            .IsRequired();
        usuario.Property(u => u.LavaderoId).HasColumnName("lavadero_id");

        // D-06.1: vínculo con el cliente de negocio (nulo mientras no exista
        // alta/vinculación; D-05.9).
        usuario.Property(u => u.ClienteId).HasColumnName("cliente_id");
        usuario.Property(u => u.Activo).HasColumnName("activo").IsRequired();
        usuario.Property(u => u.CreadoEn)
            .HasColumnName("creado_en")
            .HasColumnType("timestamptz");

        // La credencial se localiza por nombre en el login: debe ser única.
        usuario.HasIndex(u => u.NombreUsuario).IsUnique();

        // Sin UNIQUE: un cliente de negocio podría tener más de una cuenta
        // (cardinalidad flexible, D-06.1).
        usuario.HasIndex(u => u.ClienteId);

        usuario.HasOne<Cliente>()
            .WithMany()
            .HasForeignKey(u => u.ClienteId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
