using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Pagos;

namespace WashPoints.Infrastructure.Persistencia.Configuraciones;

/// <summary>
/// Configuración EF Core de <see cref="Pago"/> (tabla `pagos`).
///
/// Sigue el patrón de <c>ConfigurarTurno</c>: se aplica desde
/// <see cref="WashPointsDbContext.OnModelCreating"/>, no via
/// <c>ApplyConfigurationsFromAssembly</c>. Ver SPECS/slice-01 §4 (T-11).
/// </summary>
public sealed class PagoConfig : IEntityTypeConfiguration<Pago>
{
    public void Configure(EntityTypeBuilder<Pago> pago)
    {
        pago.ToTable("pagos");
        pago.HasKey(p => p.Id);

        pago.Property(p => p.Id).HasColumnName("id");
        pago.Property(p => p.TurnoId).HasColumnName("turno_id").IsRequired();
        pago.Property(p => p.MpPaymentId)
            .HasColumnName("mp_payment_id")
            .HasMaxLength(128)
            .IsRequired();
        pago.Property(p => p.TipoEvento)
            .HasColumnName("tipo_evento")
            .HasConversion(new ConvertidorEnumSnakeCase<TipoEventoPago>())
            .HasMaxLength(16)
            .IsRequired();
        pago.Property(p => p.Resultado)
            .HasColumnName("resultado")
            .HasConversion(new ConvertidorEnumSnakeCase<ResultadoPago>())
            .HasMaxLength(32)
            .IsRequired();
        pago.Property(p => p.ProcesadoEn)
            .HasColumnName("procesado_en")
            .HasColumnType("timestamptz");

        // La fila se inserta en el MISMO SaveChangesAsync que la transición del
        // turno: o hay pago registrado y estado transitado, o no hay ninguno
        // (RB-1). FK con Restrict: borrar un turno no arrastra su historial.
        pago.HasOne<Turno>()
            .WithMany()
            .HasForeignKey(p => p.TurnoId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        // Idempotencia del webhook a nivel de base: un duplicado de la
        // notificación choca con UNIQUE(mp_payment_id) y responde 200 sin
        // re-transicionar.
        pago.HasIndex(p => p.MpPaymentId).IsUnique();

        // Un turno no puede tener dos pagos asociados (RB-1).
        pago.HasIndex(p => p.TurnoId).IsUnique();
    }
}