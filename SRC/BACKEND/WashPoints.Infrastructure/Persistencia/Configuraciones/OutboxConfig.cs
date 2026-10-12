using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace WashPoints.Infrastructure.Persistencia.Configuraciones;

/// <summary>
/// Configuración EF Core de <see cref="Outbox"/> (tabla `outbox`,
/// ARCHITECTURE §4.1). Se aplica desde
/// <see cref="WashPointsDbContext.OnModelCreating"/>. Ver SPECS/slice-03 T-34.
/// </summary>
public sealed class OutboxConfig : IEntityTypeConfiguration<Outbox>
{
    public void Configure(EntityTypeBuilder<Outbox> outbox)
    {
        outbox.ToTable("outbox");
        outbox.HasKey(o => o.Id);

        outbox.Property(o => o.Id).HasColumnName("id");
        outbox.Property(o => o.Tipo).HasColumnName("tipo").IsRequired();

        // El payload es el evento de ocupación serializado a JSON.
        outbox.Property(o => o.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();

        outbox.Property(o => o.PublicadoEn)
            .HasColumnName("publicado_en")
            .HasColumnType("timestamptz");

        outbox.Property(o => o.CreadoEn)
            .HasColumnName("creado_en")
            .HasColumnType("timestamptz")
            .IsRequired();

        // El publicador solo mira las filas no publicadas: índice PARCIAL
        // (WHERE publicado_en IS NULL) para no escanear el histórico.
        outbox.HasIndex(o => o.PublicadoEn)
            .HasFilter(@"""publicado_en"" IS NULL")
            .HasDatabaseName("outbox_no_publicados");
    }
}
