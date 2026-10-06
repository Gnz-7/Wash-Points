using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Tiempo;

namespace WashPoints.Infrastructure.Persistencia;

/// <summary>
/// Contexto de EF Core. Todas las tablas viven en este esquema.
///
/// La regla de módulos (ARCHITECTURE §2.2) —ninguno lee tablas de otro— no se
/// puede imponer aquí, porque un único contexto las ve todas. Se hace cumplir
/// en <c>OcupacionService</c>, la única puerta de escritura sobre `turnos`.
/// </summary>
public sealed class WashPointsDbContext : DbContext
{
    public WashPointsDbContext(DbContextOptions<WashPointsDbContext> opciones)
        : base(opciones)
    {
    }

    public DbSet<Turno> Turnos => Set<Turno>();
    public DbSet<Lavadero> Lavaderos => Set<Lavadero>();
    public DbSet<Puesto> Puestos => Set<Puesto>();
    public DbSet<TipoLavado> TiposLavado => Set<TipoLavado>();
    public DbSet<Cliente> Clientes => Set<Cliente>();

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        base.OnModelCreating(modelo);

        ConfigurarTurno(modelo.Entity<Turno>());
        ConfigurarCatalogo(modelo);
    }

    private static void ConfigurarTurno(EntityTypeBuilder<Turno> turno)
    {
        turno.ToTable("turnos");
        turno.HasKey(t => t.Id);

        turno.Property(t => t.Id).HasColumnName("id");
        turno.Property(t => t.LavaderoId).HasColumnName("lavadero_id").IsRequired();
        turno.Property(t => t.PuestoId).HasColumnName("puesto_id").IsRequired();
        turno.Property(t => t.ClienteId).HasColumnName("cliente_id").IsRequired();
        turno.Property(t => t.TipoLavadoId).HasColumnName("tipo_lavado_id").IsRequired();

        // Relaciones declaradas en ARCHITECTURE §4.1. `lavadero_id` y
        // `cliente_id` quedan como uuid sueltos, sin constraint: ver §10.7.
        turno.HasOne<Puesto>()
            .WithMany()
            .HasForeignKey(t => t.PuestoId)
            .IsRequired()
            // Borrar un puesto no puede arrastrar turnos históricos: son datos
            // de negocio y de pago. El admin desactiva el puesto (activo=false).
            .OnDelete(DeleteBehavior.Restrict);

        turno.HasOne<TipoLavado>()
            .WithMany()
            .HasForeignKey(t => t.TipoLavadoId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        // La columna `rango` guarda hora LOCAL sin zona (ARCHITECTURE §4.2 y
        // §10.6): así el EXCLUDE compara "las 10:00" y no instantes de UTC.
        // NpgsqlRange<DateTime> mapearía a tstzrange por defecto, por eso el
        // tipo de columna se fija explícitamente.
        //
        // Este convertidor es la ÚNICA pieza que traduce entre el dominio, que
        // vive en UTC, y la base, que guarda hora local. Ver §10.6.
        turno.Property(t => t.Rango)
            .HasColumnName("rango")
            .HasColumnType("tsrange")
            .HasConversion(ConvertidorRangoTurno.Instancia);

        turno.Property(t => t.Estado)
            .HasColumnName("estado")
            .HasConversion(new ConvertidorEnumSnakeCase<EstadoTurno>())
            .HasMaxLength(32)
            .IsRequired();

        turno.Property(t => t.Origen)
            .HasColumnName("origen")
            .HasConversion(new ConvertidorEnumSnakeCase<OrigenTurno>())
            .HasMaxLength(16)
            .IsRequired();

        turno.Property(t => t.PrecioSena)
            .HasColumnName("precio_sena")
            .HasColumnType("numeric(10,2)");

        turno.Property(t => t.MpPreferenceId)
            .HasColumnName("mp_preference_id")
            .HasMaxLength(128);

        turno.Property(t => t.CreadoEn)
            .HasColumnName("creado_en")
            .HasColumnType("timestamptz");

        turno.Property(t => t.ConfirmadoEn)
            .HasColumnName("confirmado_en")
            .HasColumnType("timestamptz");

        turno.Property(t => t.CerradoEn)
            .HasColumnName("cerrado_en")
            .HasColumnType("timestamptz");

        // Apoyo a la agenda del día, que filtra por lavadero + estado sin
        // mirar un puesto concreto. El EXCLUDE ya indexa por su cuenta.
        turno.HasIndex(t => new { t.LavaderoId, t.Estado });
    }

    private static void ConfigurarCatalogo(ModelBuilder modelo)
    {
        // Tablas mínimas para resolver las FK de `turnos`. El esquema definitivo
        // está pendiente: ver DOCS/ARCHITECTURE.md §10.7.
        var lavadero = modelo.Entity<Lavadero>();
        lavadero.ToTable("lavaderos");
        lavadero.HasKey(x => x.Id);
        lavadero.Property(x => x.Id).HasColumnName("id");
        lavadero.Property(x => x.Nombre).HasColumnName("nombre").HasMaxLength(120).IsRequired();
        lavadero.Property(x => x.Activo).HasColumnName("activo").IsRequired();

        var puesto = modelo.Entity<Puesto>();
        puesto.ToTable("puestos");
        puesto.HasKey(x => x.Id);
        puesto.Property(x => x.Id).HasColumnName("id");
        puesto.Property(x => x.LavaderoId).HasColumnName("lavadero_id").IsRequired();
        puesto.Property(x => x.Nombre).HasColumnName("nombre").HasMaxLength(80).IsRequired();
        puesto.Property(x => x.Activo).HasColumnName("activo").IsRequired();
        puesto.HasIndex(x => new { x.LavaderoId, x.Nombre }).IsUnique();

        var tipo = modelo.Entity<TipoLavado>();
        tipo.ToTable("tipos_lavado");
        tipo.HasKey(x => x.Id);
        tipo.Property(x => x.Id).HasColumnName("id");
        tipo.Property(x => x.LavaderoId).HasColumnName("lavadero_id").IsRequired();
        tipo.Property(x => x.Nombre).HasColumnName("nombre").HasMaxLength(80).IsRequired();
        tipo.Property(x => x.DuracionMinutos).HasColumnName("duracion_minutos").IsRequired();
        tipo.Property(x => x.PrecioTotal).HasColumnName("precio_total").HasColumnType("numeric(10,2)");
        tipo.Property(x => x.PrecioSena).HasColumnName("precio_sena").HasColumnType("numeric(10,2)");
        tipo.Property(x => x.Activo).HasColumnName("activo").IsRequired();
        tipo.HasIndex(x => new { x.LavaderoId, x.Nombre }).IsUnique();

        var cliente = modelo.Entity<Cliente>();
        cliente.ToTable("clientes");
        cliente.HasKey(x => x.Id);
        cliente.Property(x => x.Id).HasColumnName("id");
        cliente.Property(x => x.Nombre).HasColumnName("nombre").HasMaxLength(120).IsRequired();
    }
}
