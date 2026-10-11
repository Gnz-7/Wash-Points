using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WashPoints.Api;
using WashPoints.Application.Pagos;
using WashPoints.Application.ReservarTurno;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Puertos;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.MercadoPago;
using WashPoints.Infrastructure.Pagos;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.Infrastructure.Tiempo;

var builder = WebApplication.CreateBuilder(args);

// Persistencia y servicios del dominio. El DbContext es scoped, igual que los
// servicios que dependen de él: un "request HTTP" == un contexto == una
// transacción.
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException(
        "Falta la connection string 'Postgres' en la configuración.");

builder.Services.AddDbContext<WashPointsDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<IDisponibilidadConsulta, DisponibilidadConsulta>();
builder.Services.AddScoped<IOcupacionRepositorio, OcupacionRepositorio>();
builder.Services.AddScoped<IUnidadDeTrabajo, UnidadDeTrabajo>();
builder.Services.AddScoped<IConsultaCatalogo, ConsultaCatalogo>();
builder.Services.AddScoped<ReservaServicio>();
builder.Services.AddScoped<WebhookPagoServicio>();
builder.Services.AddScoped<RespaldoYExpiracionServicio>();

// El worker de respaldo/expiración y la pasarela real se registran en T-17/T-21.
builder.Services.AddScoped<IPasarelaPagos, PasarelaPagosMercadoPago>();

// Configuración del ciclo de vida de la seña (sección "Pagos").
var opcionesPago = builder.Configuration.GetSection("Pagos").Get<OpcionesPago>()
    ?? new OpcionesPago();
builder.Services.AddSingleton(opcionesPago);

builder.Services.AddSingleton(TimeProvider.System);

// Worker de conciliación (T-21): respaldo a los 3 min y expiración del TTL.
builder.Services.AddHostedService<WashPoints.Api.WorkerReservas>();

// Validador de firma del webhook IPN. El secreto va por variable de entorno
// (Pagos:WebhookSecret, nunca en el repo, AGENTS §3.4). Si falta, se usa un
// secreto aleatorio de arranque: toda firma entrante se rechaza (401), que es
// el default seguro; se avisa por log. Tolerancia configurable (default 5 min).
var webhookSecret = builder.Configuration["Pagos:WebhookSecret"];
var webhookSecretEfectivo = string.IsNullOrWhiteSpace(webhookSecret)
    ? Guid.NewGuid().ToString("N")
    : webhookSecret;
var webhookToleranciaSegundos = builder.Configuration.GetValue<int?>("Pagos:WebhookToleranciaSegundos") ?? 300;
builder.Services.AddSingleton(new WebhookSignatureValidator(
    webhookSecretEfectivo,
    TimeSpan.FromSeconds(webhookToleranciaSegundos),
    TimeProvider.System));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Consulta de disponibilidad (T-07, US-C05 CA-5.1). Devuelve los intervalos
// ocupados en la hora LOCAL del lavadero: el contrato interno vive en UTC
// (§10.6), y este endpoint es el que expone a los clientes, que piensan en su
// huso. Solo aparecen turnos que ocupan puesto (confirmado/en_curso) y el
// pendiente_pago vigente (slice-01, RB-2): la consulta los filtra.
//
// Parámetros `desde`/`hasta` en ISO 8601 de la zona Argentina (sin zona), por
// ejemplo 2026-10-10T10:00:00. Se interpretan como hora local y se convierten
// a UTC antes de cruzar el contrato del dominio.
app.MapGet("/api/lavaderos/{id:guid}/disponibilidad", async (
    Guid id,
    DateTime? desde,
    DateTime? hasta,
    IDisponibilidadConsulta consulta,
    CancellationToken ct) =>
{
    if (desde is null || hasta is null)
    {
        return Results.BadRequest(new { error = "Se requieren los parámetros 'desde' y 'hasta' (ISO 8601)." });
    }

    try
    {
        var ocupados = await consulta.ObtenerOcupadosAsync(
            id,
            ConversorHorario.AUtc(desde.Value),
            ConversorHorario.AUtc(hasta.Value),
            ct);

        // El cliente ve hora local: se convierte el intervalo (UTC en el
        // dominio) a la zona del lavadero antes de serializar.
        return Results.Ok(ocupados.Select(i => new
        {
            turnoId = i.TurnoId,
            puestoId = i.PuestoId,
            origen = i.Origen switch
            {
                OrigenTurno.App => "app",
                _ => "espontanea",
            },
            desde = ConversorHorario.ALocal(i.InicioUtc),
            hasta = ConversorHorario.ALocal(i.FinUtc),
        }));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
.WithName("ConsultarDisponibilidad")
.WithOpenApi();

// Reserva con seña (T-15, US-C01). Crea el turno pendiente_pago que bloquea el
// intervalo, pide la preferencia a la pasarela y devuelve 201. Los errores del
// flujo de negocio se mapean a códigos explícitos:
//   TurnoConflictoException -> 409 slot_ya_ocupado
//   PasarelaNoDisponibleException -> 503 (la reserva no se persistió)
//   request inválido -> 400
app.MapPost("/api/lavaderos/{id:guid}/turnos", async (
    Guid id,
    SolicitudReservaHttp cuerpo,
    ReservaServicio servicio,
    CancellationToken ct) =>
{
    if (cuerpo.PuestoId == Guid.Empty ||
        cuerpo.ClienteId == Guid.Empty ||
        cuerpo.TipoLavadoId == Guid.Empty ||
        string.IsNullOrWhiteSpace(cuerpo.Inicio))
    {
        return Results.BadRequest(new { error = "Faltan datos: puestoId, tipoLavadoId, clienteId e inicio son obligatorios." });
    }

    if (!DateTime.TryParse(
            cuerpo.Inicio,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var inicioLocal))
    {
        return Results.BadRequest(new { error = "El campo 'inicio' debe ser una fecha ISO 8601." });
    }

    var solicitud = new SolicitudReserva(
        LavaderoId: id,
        PuestoId: cuerpo.PuestoId,
        ClienteId: cuerpo.ClienteId,
        TipoLavadoId: cuerpo.TipoLavadoId,
        InicioUtc: ConversorHorario.AUtc(inicioLocal));

    try
    {
        var turno = await servicio.ReservarTurnoAsync(solicitud, ct);
        return Results.Created($"/api/turnos/{turno.Id}", new
        {
            id = turno.Id,
            estado = EstadoAJson(turno.Estado),
            puestoId = turno.PuestoId,
            mpPreferenceId = turno.MpPreferenceId,
            precioSena = turno.PrecioSena,
            desde = ConversorHorario.ALocal(turno.InicioUtc),
            hasta = ConversorHorario.ALocal(turno.FinUtc),
        });
    }
    catch (TurnoConflictoException ex)
    {
        return Results.Conflict(new { error = "slot_ya_ocupado", detalle = ex.Message });
    }
    catch (PasarelaNoDisponibleException)
    {
        return Results.Json(
            new { error = "pasarela_no_disponible" },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
.WithName("ReservarTurno")
.WithOpenApi();

// Webhook IPN de Mercado Pago (T-17..T-19). Es la ÚNICA puerta de confirmación
// de un turno (RB-1, AGENTS §2.4): el retorno del navegador del cliente no
// confirma nada. Secuencia: extraer el pago notificado, validar la firma HMAC
// (401 si no es válida o está fuera de tolerancia) y delegar en el servicio, que
// registra el pago y transiciona el turno de forma idempotente.
app.MapPost("/api/webhooks/mercadopago", async (
    HttpRequest request,
    WebhookSignatureValidator validador,
    WebhookPagoServicio servicio,
    CancellationToken ct) =>
{
    JsonDocument documento;
    try
    {
        documento = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
    }
    catch (JsonException)
    {
        return Results.BadRequest(new { error = "cuerpo_invalido" });
    }

    using (documento)
    {
        var raiz = documento.RootElement;
        var tipo = raiz.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()
            : null;
        var dataId = ExtraerDataId(raiz);

        if (string.IsNullOrWhiteSpace(dataId))
        {
            return Results.BadRequest(new { error = "sin_data_id" });
        }

        var xSignature = request.Headers["x-signature"].FirstOrDefault();
        var xRequestId = request.Headers["x-request-id"].FirstOrDefault();

        if (!validador.EsValida(xSignature, xRequestId, dataId))
        {
            return Results.Json(new { error = "firma_invalida" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var resultado = await servicio.ProcesarAsync(tipo, dataId, ct);
        return Results.Ok(new { resultado = resultado.ToString() });
    }
})
.WithName("WebhookMercadoPago")
.WithOpenApi();

app.Run();

/// <summary>Extrae <c>data.id</c> del IPN, que MP envía como texto o número.</summary>
static string? ExtraerDataId(JsonElement raiz)
{
    if (!raiz.TryGetProperty("data", out var data) ||
        data.ValueKind != JsonValueKind.Object ||
        !data.TryGetProperty("id", out var id))
    {
        return null;
    }

    return id.ValueKind switch
    {
        JsonValueKind.String => id.GetString(),
        JsonValueKind.Number => id.GetRawText(),
        _ => null,
    };
}

static string EstadoAJson(EstadoTurno estado) => estado switch
{
    EstadoTurno.PendientePago => "pendiente_pago",
    EstadoTurno.Confirmado => "confirmado",
    EstadoTurno.EnCurso => "en_curso",
    EstadoTurno.Finalizado => "finalizado",
    EstadoTurno.Cancelado => "cancelado",
    EstadoTurno.NoPresentado => "no_presentado",
    EstadoTurno.Expirado => "expirado",
    _ => estado.ToString().ToLowerInvariant(),
};

/// <summary>Cuerpo de la solicitud de reserva. `inicio` en hora local del lavadero.</summary>
public sealed record SolicitudReservaHttp(
    Guid PuestoId,
    Guid TipoLavadoId,
    Guid ClienteId,
    string? Inicio);

/// <summary>Punto de entrada para los tests de integración (WebApplicationFactory).</summary>
public partial class Program;
