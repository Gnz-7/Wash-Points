using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WashPoints.Domain.Comprobantes;
using WashPoints.Domain.Turnos;

namespace WashPoints.Infrastructure.Persistencia.Configuraciones;

/// <summary>
/// Configuración EF Core de <see cref="Comprobante"/> (tabla `comprobantes`,
/// ARCHITECTURE §4.1).
///
/// Sigue el patrón de <see cref="PagoConfig"/>: se aplica desde
/// <see cref="WashPointsDbContext.OnModelCreating"/>. Ver SPECS/slice-02 T-24.
/// </summary>
public sealed class ComprobanteConfig : IEntityTypeConfiguration<Comprobante>
{
    public void Configure(EntityTypeBuilder<Comprobante> comprobante)
    {
        comprobante.ToTable("comprobantes");
        comprobante.HasKey(c => c.Id);

        comprobante.Property(c => c.Id).HasColumnName("id");
        comprobante.Property(c => c.TurnoId).HasColumnName("turno_id").IsRequired();

        // SHA-256 en hex: longitud fija 64. El hash localiza el comprobante y
        // es único (dos comprobantes no comparten token).
        comprobante.Property(c => c.Hash)
            .HasColumnName("hash")
            .HasMaxLength(64)
            .IsRequired();

        // Token cifrado simétricamente (AES-GCM). Nunca se guarda el claro en
        // reposo: la columna es imprescindible para re-servir el comprobante al
        // cliente (US-C08 CA-8.4) sin exponer el secreto (ARCHITECTURE §8).
        comprobante.Property(c => c.TokenCifrado)
            .HasColumnName("token_cifrado")
            .HasColumnType("text")
            .IsRequired();

        comprobante.Property(c => c.ExpiraEn)
            .HasColumnName("expira_en")
            .HasColumnType("timestamptz")
            .IsRequired();

        comprobante.Property(c => c.UsadoAt)
            .HasColumnName("usado_at")
            .HasColumnType("timestamptz");

        comprobante.Property(c => c.ValidadoOffline)
            .HasColumnName("validado_offline")
            .IsRequired();

        comprobante.Property(c => c.CreadoEn)
            .HasColumnName("creado_en")
            .HasColumnType("timestamptz")
            .IsRequired();

        // FK con Restrict: borrar un turno no arrastra su comprobante histórico.
        comprobante.HasOne<Turno>()
            .WithMany()
            .HasForeignKey(c => c.TurnoId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        // Un turno confirmado tiene exactamente un comprobante (US-C08 CA-8.1).
        comprobante.HasIndex(c => c.TurnoId).IsUnique();

        // El canje localiza por hash; la unicidad impide reutilizar un token
        // para dos comprobantes.
        comprobante.HasIndex(c => c.Hash).IsUnique();
    }
}
