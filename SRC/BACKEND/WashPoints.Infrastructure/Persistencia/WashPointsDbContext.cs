using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Comprobantes;
using WashPoints.Domain.Reembolsos;
using WashPoints.Domain.Seguridad;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Pagos;
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
    public DbSet<FranjaAtencion> HorariosAtencion => Set<FranjaAtencion>();
    public DbSet<PuestoTipoLavado> PuestosTiposLavado => Set<PuestoTipoLavado>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Pago> Pagos => Set<Pago>();
    public DbSet<Comprobante> Comprobantes => Set<Comprobante>();
    public DbSet<Outbox> Outbox => Set<Outbox>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Reembolso> Reembolsos => Set<Reembolso>();

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        base.OnModelCreating(modelo);

        ConfigurarTurno(modelo.Entity<Turno>());
        ConfigurarReembolso(modelo.Entity<Reembolso>());
        modelo.ApplyConfiguration(new Configuraciones.PagoConfig());
        modelo.ApplyConfiguration(new Configuraciones.ComprobanteConfig());
        modelo.ApplyConfiguration(new Configuraciones.OutboxConfig());
        modelo.ApplyConfiguration(new Configuraciones.UsuarioConfig());
        ConfigurarCatalogo(modelo);
    }

    /// <summary>
    /// Reembolso asociado a un turno cancelado por el cliente
    /// (SPECS/slice-14 D-14.7). El monto queda congelado: la seña del turno ya
    /// está fijada y lo que se le debe al cliente no se recalcula después.
    /// Un turno tiene a lo sumo un reembolso, así que <c>turno_id</c> es único.
    /// </summary>
    private static void ConfigurarReembolso(EntityTypeBuilder<Reembolso> reembolso)
    {
        reembolso.ToTable("reembolsos");
        reembolso.HasKey(r => r.Id);

        reembolso.Property(r => r.Id).HasColumnName("id");
        reembolso.Property(r => r.TurnoId).HasColumnName("turno_id").IsRequired();

        reembolso.Property(r => r.Estado)
            .HasColumnName("estado")
            .HasConversion(new ConvertidorEnumSnakeCase<EstadoReembolso>())
            .HasMaxLength(16)
            .IsRequired();

        reembolso.Property(r => r.Motivo)
            .HasColumnName("motivo")
            .HasConversion(new ConvertidorEnumSnakeCase<MotivoReembolso>())
            .HasMaxLength(32)
            .IsRequired();

        reembolso.Property(r => r.Monto)
            .HasColumnName("monto")
            .HasColumnType("numeric(10,2)")
            .IsRequired();

        reembolso.Property(r => r.CreadoEn)
            .HasColumnName("creado_en")
            .HasColumnType("timestamptz")
            .IsRequired();

        reembolso.Property(r => r.ProcesadoEn)
            .HasColumnName("procesado_en")
            .HasColumnType("timestamptz");

        reembolso.Property(r => r.Intentos)
            .HasColumnName("intentos")
            .HasDefaultValue(0)
            .IsRequired();

        reembolso.Property(r => r.ProximoIntentoEn)
            .HasColumnName("proximo_intento_en")
            .HasColumnType("timestamptz");

        reembolso.Property(r => r.ReclamadoEn)
            .HasColumnName("reclamado_en")
            .HasColumnType("timestamptz");

        reembolso.Property(r => r.UltimoError)
            .HasColumnName("ultimo_error");

        reembolso.Property(r => r.MpRefundId)
            .HasColumnName("mp_refund_id")
            .HasMaxLength(64);

        reembolso.HasIndex(r => r.TurnoId).IsUnique();

        // Cola de trabajo del slice de pagos: barre los pendientes por antigüedad.
        reembolso.HasIndex(r => new { r.Estado, r.CreadoEn });

        // Reconciliación: un reintegro de la pasarela no puede quedar asociado a
        // dos filas. Parcial para no colisionar con los pendientes sin id.
        reembolso.HasIndex(r => r.MpRefundId)
            .IsUnique()
            .HasFilter("mp_refund_id IS NOT NULL");

        // El worker busca sólo pendientes con el próximo intento vencido.
        reembolso.HasIndex(r => r.ProximoIntentoEn)
            .HasFilter("estado = 'pendiente'");
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

        // Autoría de la cancelación (SPECS/slice-14 D-14.5). Nullable: sólo un
        // turno cancelado tiene autor. La columna distingue la baja pedida por
        // el cliente de la que ejecutó la agenda (D-14.9).
        turno.Property(t => t.CanceladoPor)
            .HasColumnName("cancelado_por")
            .HasConversion(new ConvertidorEnumSnakeCase<AutorCancelacion>())
            .HasMaxLength(16);

        turno.Property(t => t.PrecioSena)
            .HasColumnName("precio_sena")
            .HasColumnType("numeric(10,2)");

        // Snapshot del precio total al crear el turno (SPECS/slice-13 D-13.5).
        // No se lee del catálogo al calcular métricas: si el tipo de lavado
        // cambia de precio, los turnos ya devengados conservan el suyo.
        turno.Property(t => t.PrecioTotal)
            .HasColumnName("precio_total")
            .HasColumnType("numeric(10,2)")
            .IsRequired();

        turno.Property(t => t.MpPreferenceId)
            .HasColumnName("mp_preference_id")
            .HasMaxLength(128);

        // TTL de la seña (slice-01, SPECS/slice-01 §3): vence el bloqueo del
        // pendiente_pago. Nullable porque un turno confirmado/cancelado no tiene
        // vencimiento pendiente.
        turno.Property(t => t.ExpiraEn)
            .HasColumnName("expira_en")
            .HasColumnType("timestamptz");

        // Idempotencia de la reserva: la unicidad es un índice PARCIAL (solo
        // WHERE idempotency_key IS NOT NULL), para que la clave no contamine a
        // turnos sin intento de reserva. Ver SPECS/slice-01 §3 (23P01/23505).
        turno.Property(t => t.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(64);
        turno.HasIndex(t => t.IdempotencyKey)
            .IsUnique()
            .HasFilter(@"""idempotency_key"" IS NOT NULL")
            .HasDatabaseName("turnos_idempotency_unicos");

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
        lavadero.ToTable("lavaderos", tabla =>
        {
            // Las coordenadas son opcionales, pero nunca a medias: la búsqueda
            // por proximidad solo considera lavaderos con ambas (D-08.1).
            tabla.HasCheckConstraint(
                "ck_lavaderos_latitud", "latitud IS NULL OR (latitud >= -90 AND latitud <= 90)");
            tabla.HasCheckConstraint(
                "ck_lavaderos_longitud", "longitud IS NULL OR (longitud >= -180 AND longitud <= 180)");
            tabla.HasCheckConstraint(
                "ck_lavaderos_ubicacion_completa", "(latitud IS NULL) = (longitud IS NULL)");
        });
        lavadero.HasKey(x => x.Id);
        lavadero.Property(x => x.Id).HasColumnName("id");
        lavadero.Property(x => x.Nombre).HasColumnName("nombre").HasMaxLength(120).IsRequired();
        lavadero.Property(x => x.Activo).HasColumnName("activo").IsRequired();
        lavadero.Property(x => x.Direccion).HasColumnName("direccion").HasMaxLength(200);
        lavadero.Property(x => x.Latitud).HasColumnName("latitud");
        lavadero.Property(x => x.Longitud).HasColumnName("longitud");
        lavadero.HasIndex(x => new { x.Latitud, x.Longitud })
            .HasFilter(@"""latitud"" IS NOT NULL")
            .HasDatabaseName("ix_lavaderos_ubicacion");

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

        // Horarios de atención (slice-17, R-1). El día es ISO-8601 (1 = lunes) y
        // las horas son locales del lavadero: `time` sin zona a propósito,
        // porque son reglas de calendario, no instantes (D-17.1).
        var franja = modelo.Entity<FranjaAtencion>();
        franja.ToTable("horarios_atencion", tabla =>
        {
            tabla.HasCheckConstraint(
                "ck_horarios_atencion_dia_semana", "dia_semana BETWEEN 1 AND 7");
            tabla.HasCheckConstraint(
                "ck_horarios_atencion_rango", "apertura < cierre");
        });
        franja.HasKey(x => x.Id);
        franja.Property(x => x.Id).HasColumnName("id");
        franja.Property(x => x.LavaderoId).HasColumnName("lavadero_id").IsRequired();
        franja.Property(x => x.DiaSemana).HasColumnName("dia_semana").IsRequired();
        franja.Property(x => x.Apertura).HasColumnName("apertura").IsRequired();
        franja.Property(x => x.Cierre).HasColumnName("cierre").IsRequired();
        franja.Property(x => x.Activo).HasColumnName("activo").IsRequired();
        franja.Property(x => x.CreadoEn).HasColumnName("creado_en").IsRequired();
        franja.HasIndex(x => new { x.LavaderoId, x.DiaSemana, x.Apertura })
            .IsUnique()
            .HasFilter(@"""activo""")
            .HasDatabaseName("ix_horarios_atencion_franja");
        franja.HasIndex(x => new { x.LavaderoId, x.DiaSemana })
            .HasFilter(@"""activo""")
            .HasDatabaseName("ix_horarios_atencion_lavadero_dia");

        // Compatibilidad puesto ↔ tipo de lavado (slice-17, R-4). Sin filas, el
        // puesto atiende todos los servicios (D-17.7).
        var puente = modelo.Entity<PuestoTipoLavado>();
        puente.ToTable("puestos_tipos_lavado");
        puente.HasKey(x => new { x.PuestoId, x.TipoLavadoId });
        puente.Property(x => x.PuestoId).HasColumnName("puesto_id").IsRequired();
        puente.Property(x => x.TipoLavadoId).HasColumnName("tipo_lavado_id").IsRequired();
        puente.HasOne<Puesto>()
            .WithMany()
            .HasForeignKey(x => x.PuestoId)
            .OnDelete(DeleteBehavior.Cascade);
        puente.HasOne<TipoLavado>()
            .WithMany()
            .HasForeignKey(x => x.TipoLavadoId)
            .OnDelete(DeleteBehavior.Cascade);

        var cliente = modelo.Entity<Cliente>();
        cliente.ToTable("clientes");
        cliente.HasKey(x => x.Id);
        cliente.Property(x => x.Id).HasColumnName("id");
        cliente.Property(x => x.Nombre).HasColumnName("nombre").HasMaxLength(120).IsRequired();
    }
}
