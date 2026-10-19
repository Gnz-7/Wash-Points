using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using WashPoints.Api;
using WashPoints.Api.Hubs;
using WashPoints.Api.Seguridad;
using WashPoints.Application.Agenda;
using WashPoints.Application.Catalogo;
using WashPoints.Application.Comprobantes;
using WashPoints.Application.Espontanea;
using WashPoints.Application.Metricas;
using WashPoints.Application.Pagos;
using WashPoints.Application.ReservarTurno;
using WashPoints.Application.Seguridad;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Comprobantes;
using WashPoints.Domain.Metricas;
using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Puertos;
using WashPoints.Domain.Seguridad;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Comprobantes;
using WashPoints.Infrastructure.MercadoPago;
using WashPoints.Infrastructure.Pagos;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.Infrastructure.Seguridad;
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

// Gestión de puestos y agenda (CU-05/CU-06, slice-04).
builder.Services.AddScoped<IPuestoRepositorio, PuestoRepositorio>();
builder.Services.AddScoped<IConsultaAgenda, ConsultaAgenda>();
builder.Services.AddScoped<GestionPuestosServicio>();
builder.Services.AddScoped<AgendaServicio>();
builder.Services.AddScoped<CierreAgendaServicio>();
builder.Services.AddScoped<IMetricasConsulta, ConsultaMetricas>();
builder.Services.AddScoped<MetricasServicio>();
builder.Services.AddScoped<ReservaServicio>();
builder.Services.AddScoped<WebhookPagoServicio>();
builder.Services.AddScoped<RespaldoYExpiracionServicio>();

// Demanda espontánea (CU-07, slice-03): escribe el cliente anónimo y la outbox
// por el mismo Change Tracker que el turno, en un único commit (D8/D12).
builder.Services.AddScoped<IRegistroClientes, RegistroClientes>();
builder.Services.AddScoped<IOutbox, OutboxRepositorio>();
builder.Services.AddScoped<RegistrarDemandaEspontaneaServicio>();

// El worker de respaldo/expiración y la pasarela real se registran en T-17/T-21.
builder.Services.AddScoped<IPasarelaPagos, PasarelaPagosMercadoPago>();

// Configuración del ciclo de vida de la seña (sección "Pagos").
var opcionesPago = builder.Configuration.GetSection("Pagos").Get<OpcionesPago>()
    ?? new OpcionesPago();
builder.Services.AddSingleton(opcionesPago);

// Ventana de llegada del cierre de agenda (sección "Agenda", slice-04 T-47).
var opcionesAgenda = builder.Configuration.GetSection(OpcionesAgenda.Seccion).Get<OpcionesAgenda>()
    ?? new OpcionesAgenda();
builder.Services.AddSingleton(opcionesAgenda);

// Comprobantes (RB-3, slice-02): generador de tokens, cifrado del token en
// reposo y emisión enganchada a la confirmación de la seña. La clave AES-256 va
// por variable de entorno (Comprobantes:ClaveCifrado, nunca en el repo,
// AGENTS §3.4); si falta, la emisión falla de forma explícita (CU-04).
var opcionesComprobante = builder.Configuration.GetSection("Comprobantes").Get<OpcionesComprobante>()
    ?? new OpcionesComprobante();
builder.Services.AddSingleton(opcionesComprobante);
builder.Services.AddSingleton<IGeneradorComprobante, GeneradorComprobante>();
builder.Services.AddSingleton<ICifradorComprobante>(
    _ => new CifradorComprobanteAesGcm(opcionesComprobante.ClaveCifrado));
builder.Services.AddScoped<IEmisorComprobante>(servicios => new EmisorComprobantes(
    servicios.GetRequiredService<IGeneradorComprobante>(),
    servicios.GetRequiredService<ICifradorComprobante>(),
    servicios.GetRequiredService<WashPointsDbContext>(),
    opcionesComprobante.TtlVigenciaHoras));
builder.Services.AddScoped<IComprobanteRepositorio, ComprobanteRepositorio>();
builder.Services.AddScoped<EntregaComprobanteServicio>();
builder.Services.AddScoped<CanjeComprobanteServicio>();
builder.Services.AddScoped<VigentesComprobanteServicio>();
builder.Services.AddScoped<ReconciliacionComprobantesServicio>();

// Autenticación (CU-10, slice-05 T-53): login que emite la sesión JWT. La clave
// de firma va por variable de entorno (Jwt:ClaveSecreta, nunca en el repo,
// AGENTS §3.4); si falta, se usa una clave aleatoria de arranque (default
// seguro: ninguna sesión previa se valida hasta configurarla).
var opcionesJwt = builder.Configuration.GetSection("Jwt").Get<OpcionesJwt>() ?? new OpcionesJwt();
if (string.IsNullOrWhiteSpace(opcionesJwt.ClaveSecreta))
{
    opcionesJwt.ClaveSecreta = Guid.NewGuid().ToString("N");
}

builder.Services.AddSingleton(opcionesJwt);
builder.Services.AddSingleton<IEmisorToken>(servicios => new EmisorTokenJwt(
    opcionesJwt.ClaveSecreta,
    opcionesJwt.Emisor,
    opcionesJwt.Audiencia,
    opcionesJwt.MinutosVida,
    servicios.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<IHasheadorContrasena, HasheadorContrasenaPbkdf2>();
builder.Services.AddScoped<IUsuarioRepositorio, UsuarioRepositorio>();
builder.Services.AddScoped<AutenticacionServicio>();

// Identidad server-side (slice-05, CU-10): el bearer se valida en el pipeline y
// la autorización se resuelve por claims. La Api es la única autoridad; el
// cliente nunca decide permisos (ARCHITECTURE §7.1). El token se emite y se
// valida con la MISMA clave configurada (o el fallback de arranque).
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opciones =>
    {
        opciones.MapInboundClaims = false;
        opciones.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = opcionesJwt.Emisor,
            ValidateAudience = true,
            ValidAudience = opcionesJwt.Audiencia,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opcionesJwt.ClaveSecreta)),
            ValidateLifetime = true,
        };
    });

// Políticas de autorización (§5.2). `Administrador` cubre los endpoints de
// administración sin lavadero en ruta; `PropietarioDelLavadero` exige, además,
// que el `lavaderoId` de la sesión coincida con el `{id}` de la ruta (D-05.7).
builder.Services.AddAuthorization(opciones =>
{
    opciones.AddPolicy(Politicas.Administrador, politica =>
        politica.RequireClaim(ClaimsWashPoints.Rol, ClaimsWashPoints.Administrador));

    opciones.AddPolicy(Politicas.PropietarioDelLavadero, politica =>
    {
        politica.RequireClaim(ClaimsWashPoints.Rol, ClaimsWashPoints.Administrador);
        politica.AddRequirements(new RequisitoPropietarioLavadero());
    });
});
builder.Services.AddSingleton<IAuthorizationHandler, ManejadorPropietarioLavadero>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, ManejadorResultadoAutorizacion>();

builder.Services.AddSingleton(TimeProvider.System);

// Worker de conciliación (T-21): respaldo a los 3 min y expiración del TTL.
builder.Services.AddHostedService<WashPoints.Api.WorkerReservas>();

// Worker de cierre de agenda (T-47): confirmado vencido -> no_presentado.
builder.Services.AddHostedService<WashPoints.Api.WorkerAgenda>();

// Sincronización en tiempo real (RB-2, slice-03 T-38): SignalR más el
// publicador de la outbox. El publicador es scoped (usa el DbContext) y su
// worker lo bombea en cada tick, en su propio scope.
builder.Services.AddSignalR();
builder.Services.AddScoped<PublicadorOutbox>();
builder.Services.AddHostedService<WashPoints.Api.PublicadorOutboxWorker>();

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

// Pipeline de identidad: autenticar (valida el bearer) y autorizar (policies).
app.UseAuthentication();
app.UseAuthorization();

// Hub de disponibilidad en tiempo real (RB-2): el cliente se une al grupo de su
// lavadero y recibe `disponibilidadActualizada` cuando cambia la ocupación.
app.MapHub<DisponibilidadHub>("/hubs/disponibilidad");

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
.RequireAuthorization()
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
.RequireAuthorization()
.WithOpenApi();

// Demanda espontánea (CU-07, slice-03 T-36): el local registra a un cliente que
// llega sin turno. El turno nace `en_curso`, sin seña ni comprobante (RB-1 no
// aplica a la espontánea, D1/D9). La ocupación y el evento de outbox se
// persisten juntos (RB-2), de modo que la disponibilidad se sincroniza al
// instante. Errores: solapamiento -> 409 slot_ya_ocupado; datos inválidos -> 400.
app.MapPost("/api/lavaderos/{id:guid}/turnos-espontaneos", async (
    Guid id,
    SolicitudEspontaneaHttp cuerpo,
    RegistrarDemandaEspontaneaServicio servicio,
    CancellationToken ct) =>
{
    if (cuerpo.PuestoId == Guid.Empty ||
        cuerpo.TipoLavadoId == Guid.Empty ||
        string.IsNullOrWhiteSpace(cuerpo.Inicio))
    {
        return Results.BadRequest(new { error = "Faltan datos: puestoId, tipoLavadoId e inicio son obligatorios." });
    }

    if (!DateTime.TryParse(
            cuerpo.Inicio,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var inicioLocal))
    {
        return Results.BadRequest(new { error = "El campo 'inicio' debe ser una fecha ISO 8601." });
    }

    var solicitud = new SolicitudEspontanea(
        LavaderoId: id,
        PuestoId: cuerpo.PuestoId,
        TipoLavadoId: cuerpo.TipoLavadoId,
        InicioUtc: ConversorHorario.AUtc(inicioLocal));

    try
    {
        var turno = await servicio.RegistrarAsync(solicitud, ct);
        return Results.Created($"/api/turnos/{turno.Id}", new
        {
            id = turno.Id,
            estado = EstadoAJson(turno.Estado),
            puestoId = turno.PuestoId,
            desde = ConversorHorario.ALocal(turno.InicioUtc),
            hasta = ConversorHorario.ALocal(turno.FinUtc),
        });
    }
    catch (TurnoConflictoException ex)
    {
        return Results.Conflict(new { error = "slot_ya_ocupado", detalle = ex.Message });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
.WithName("RegistrarDemandaEspontanea")
.RequireAuthorization(Politicas.Administrador, Politicas.PropietarioDelLavadero)
.WithOpenApi();

// Comprobante del turno (RB-3, US-C08 CA-8.4): el cliente recupera el QR y el
// código de respaldo desde el servidor, sin depender de push. 404 si el turno
// no existe o todavía no fue confirmado (no tiene comprobante).
app.MapGet("/api/turnos/{id:guid}/comprobante", async (
    Guid id,
    ClaimsPrincipal usuario,
    EntregaComprobanteServicio servicio,
    CancellationToken ct) =>
{
    // D-06.3: el turnoId no es control de acceso; la propiedad sale del token.
    var comprobante = await servicio.ObtenerAsync(id, IdentidadDelToken.De(usuario), ct);
    if (comprobante is null)
    {
        return Results.NotFound(new { error = "comprobante_no_encontrado" });
    }

    return Results.Ok(new
    {
        qr = comprobante.PayloadQr,
        codigo = comprobante.CodigoRespaldo,
        expiraEn = comprobante.ExpiraEn,
    });
})
.WithName("ObtenerComprobante")
.RequireAuthorization()
.WithOpenApi();

// Canje del comprobante en destino (RB-3, US-A02/US-A04): el panel del
// administrador valida el QR o el código de respaldo. Un canje válido marca
// usado_at y lleva el turno confirmado → en_curso en una sola transacción.
// El rechazo por otro lavadero es indistinguible de "no existe" (CA-S2.10).
app.MapPost("/api/lavaderos/{id:guid}/comprobantes/validar", async (
    Guid id,
    SolicitudValidarComprobanteHttp cuerpo,
    CanjeComprobanteServicio servicio,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(cuerpo.Comprobante))
    {
        return Results.BadRequest(new { error = "falta_comprobante" });
    }

    try
    {
        var resultado = await servicio.CanjearAsync(id, cuerpo.Comprobante, ct);
        return Results.Ok(new
        {
            resultado = "canjeado",
            turnoId = resultado.TurnoId,
            estado = "en_curso",
            usadoEn = resultado.UsadoEn,
        });
    }
    catch (ComprobanteInvalidoException)
    {
        return Results.NotFound(new { error = "comprobante_no_encontrado" });
    }
    catch (ComprobanteYaUtilizadoException)
    {
        return Results.Conflict(new { error = "comprobante_ya_utilizado" });
    }
    catch (ComprobantePagoPendienteException)
    {
        return Results.Conflict(new { error = "pago_pendiente" });
    }
    catch (ComprobanteVencidoException)
    {
        return Results.Json(
            new { error = "comprobante_vencido" },
            statusCode: StatusCodes.Status410Gone);
    }
})
.WithName("ValidarComprobante")
.RequireAuthorization(Politicas.Administrador, Politicas.PropietarioDelLavadero)
.WithOpenApi();

// Caché de vigentes para el modo offline (RB-3, US-A03 CA-3.1): el admin
// sincroniza los hash de los comprobantes vigentes de sus turnos del día.
// Nunca se devuelve el token ni el ciphertext, solo los hash.
app.MapGet("/api/lavaderos/{id:guid}/comprobantes/vigentes", async (
    Guid id,
    string? fecha,
    VigentesComprobanteServicio servicio,
    CancellationToken ct) =>
{
    DateOnly dia;
    if (string.IsNullOrWhiteSpace(fecha))
    {
        dia = DateOnly.FromDateTime(ConversorHorario.ALocal(DateTime.UtcNow));
    }
    else if (!DateOnly.TryParseExact(
                 fecha,
                 "yyyy-MM-dd",
                 System.Globalization.CultureInfo.InvariantCulture,
                 System.Globalization.DateTimeStyles.None,
                 out dia))
    {
        return Results.BadRequest(new { error = "fecha_invalida" });
    }

    var inicioLocal = dia.ToDateTime(TimeOnly.MinValue);
    var desdeUtc = ConversorHorario.AUtc(inicioLocal);
    var hastaUtc = ConversorHorario.AUtc(inicioLocal.AddDays(1));

    var hashes = await servicio.ListarAsync(id, desdeUtc, hastaUtc, ct);
    return Results.Ok(new { fecha = dia.ToString("yyyy-MM-dd"), hashes });
})
.WithName("ComprobantesVigentes")
.RequireAuthorization(Politicas.Administrador, Politicas.PropietarioDelLavadero)
.WithOpenApi();

// Reconciliación de canjes offline (RB-3, US-A03 CA-3.4/CA-3.5): el servidor es
// la autoridad. Cada canje del lote se valida de forma independiente; los que no
// resuelven válidos NO alteran el turno y se devuelven como rechazados.
app.MapPost("/api/lavaderos/{id:guid}/comprobantes/reconciliar", async (
    Guid id,
    SolicitudReconciliacionHttp cuerpo,
    ReconciliacionComprobantesServicio servicio,
    CancellationToken ct) =>
{
    var entradas = new List<CanjeOffline>();
    foreach (var canje in cuerpo.Canjes ?? [])
    {
        var hash = canje.Hash;
        var validadoEn = canje.ValidadoEn;
        if (string.IsNullOrWhiteSpace(hash) || validadoEn is null)
        {
            continue;
        }

        entradas.Add(new CanjeOffline(hash, validadoEn.Value.UtcDateTime));
    }

    var resultado = await servicio.ReconciliarAsync(id, entradas, ct);

    return Results.Ok(new
    {
        aplicados = resultado.Aplicados.Select(a => new { turnoId = a.TurnoId, hash = a.Hash }),
        rechazados = resultado.Rechazados.Select(r => new { hash = r.Hash, motivo = r.Motivo }),
    });
})
.WithName("ReconciliarComprobantes")
.RequireAuthorization(Politicas.Administrador, Politicas.PropietarioDelLavadero)
.WithOpenApi();

// Gestión de puestos de lavado (CU-05, SPECS/slice-04 T-44). El admin crea,
// lista, desactiva y reactiva puestos. Desactivar un puesto con turnos vigentes
// se rechaza con 409 puesto_con_turnos_activos (D5): la regla la decide el
// servicio, no el endpoint. Un puesto de otro lavadero es indistinguible de
// inexistente (404, D-04.7).
app.MapGet("/api/lavaderos/{id:guid}/puestos", async (
    Guid id,
    GestionPuestosServicio servicio,
    CancellationToken ct) =>
{
    var puestos = await servicio.ListarAsync(id, ct);
    return Results.Ok(puestos.Select(PuestoAJson));
})
.WithName("ListarPuestos")
.RequireAuthorization(Politicas.Administrador, Politicas.PropietarioDelLavadero)
.WithOpenApi();

app.MapPost("/api/lavaderos/{id:guid}/puestos", async (
    Guid id,
    SolicitudPuestoHttp cuerpo,
    GestionPuestosServicio servicio,
    CancellationToken ct) =>
{
    try
    {
        var puesto = await servicio.CrearAsync(id, cuerpo.Nombre ?? string.Empty, ct);
        return Results.Created($"/api/lavaderos/{id}/puestos/{puesto.Id}", PuestoAJson(puesto));
    }
    catch (PuestoDuplicadoException)
    {
        return Results.Conflict(new { error = "puesto_ya_existe" });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
.WithName("CrearPuesto")
.RequireAuthorization(Politicas.Administrador, Politicas.PropietarioDelLavadero)
.WithOpenApi();

app.MapPost("/api/lavaderos/{id:guid}/puestos/{puestoId:guid}/desactivar", async (
    Guid id,
    Guid puestoId,
    GestionPuestosServicio servicio,
    CancellationToken ct) =>
{
    try
    {
        var puesto = await servicio.DesactivarAsync(id, puestoId, ct);
        return Results.Ok(PuestoAJson(puesto));
    }
    catch (PuestoNoEncontradoException)
    {
        return Results.NotFound(new { error = "puesto_no_encontrado" });
    }
    catch (PuestoConTurnosActivosException ex)
    {
        return Results.Conflict(new { error = "puesto_con_turnos_activos", detalle = ex.Message });
    }
})
.WithName("DesactivarPuesto")
.RequireAuthorization(Politicas.Administrador, Politicas.PropietarioDelLavadero)
.WithOpenApi();

app.MapPost("/api/lavaderos/{id:guid}/puestos/{puestoId:guid}/activar", async (
    Guid id,
    Guid puestoId,
    GestionPuestosServicio servicio,
    CancellationToken ct) =>
{
    try
    {
        var puesto = await servicio.ActivarAsync(id, puestoId, ct);
        return Results.Ok(PuestoAJson(puesto));
    }
    catch (PuestoNoEncontradoException)
    {
        return Results.NotFound(new { error = "puesto_no_encontrado" });
    }
})
.WithName("ActivarPuesto")
.RequireAuthorization(Politicas.Administrador, Politicas.PropietarioDelLavadero)
.WithOpenApi();

// Agenda del lavadero (CU-06, SPECS/slice-04 T-46). Lista los turnos del día en
// la hora LOCAL del lavadero (mismo contrato que disponibilidad, §10.6): `fecha`
// es un día local; `puestoId` y `estado` son filtros opcionales. Sin `fecha` se
// usa el día de hoy. Ver §5.1.
app.MapGet("/api/lavaderos/{id:guid}/agenda", async (
    Guid id,
    string? fecha,
    Guid? puestoId,
    string? estado,
    AgendaServicio servicio,
    CancellationToken ct) =>
{
    DateOnly dia;
    if (string.IsNullOrWhiteSpace(fecha))
    {
        dia = DateOnly.FromDateTime(ConversorHorario.ALocal(DateTime.UtcNow));
    }
    else if (!DateOnly.TryParseExact(
                 fecha,
                 "yyyy-MM-dd",
                 System.Globalization.CultureInfo.InvariantCulture,
                 System.Globalization.DateTimeStyles.None,
                 out dia))
    {
        return Results.BadRequest(new { error = "fecha_invalida" });
    }

    EstadoTurno? estadoFiltro = null;
    if (!string.IsNullOrWhiteSpace(estado))
    {
        if (!TryParseEstado(estado, out var parseado))
        {
            return Results.BadRequest(new { error = "estado_invalido" });
        }

        estadoFiltro = parseado;
    }

    var inicioLocal = dia.ToDateTime(TimeOnly.MinValue);
    var desdeUtc = ConversorHorario.AUtc(inicioLocal);
    var hastaUtc = ConversorHorario.AUtc(inicioLocal.AddDays(1));

    var agenda = await servicio.ObtenerAsync(id, desdeUtc, hastaUtc, puestoId, estadoFiltro, ct);

    return Results.Ok(agenda.Select(TurnoAgendaAJson));
})
.WithName("ConsultarAgenda")
.RequireAuthorization(Politicas.Administrador, Politicas.PropietarioDelLavadero)
.WithOpenApi();

// Métricas y BI (CU-09, US-A08): lecturas agregadas de solo lectura para el
// panel del administrador. El día se resuelve en hora local del lavadero
// (ARCHITECTURE §10.6) y las tres magnitudes de dinero viajan separadas: la
// seña es un adelanto y el facturado, el servicio entregado (CA-8.2).
app.MapGet("/api/lavaderos/{id:guid}/metricas/resumen", async (
    Guid id,
    string? fecha,
    MetricasServicio servicio,
    CancellationToken ct) =>
{
    if (!TryParseDiaLocal(fecha, out var dia))
    {
        return Results.BadRequest(new { error = "fecha_invalida" });
    }

    var (desdeUtc, hastaUtc) = RangoDelDiaLocal(dia);

    try
    {
        var resumen = await servicio.ObtenerResumenAsync(id, dia, desdeUtc, hastaUtc, ct);
        return Results.Ok(ResumenMetricasAJson(resumen));
    }
    catch (VentanaInvalidaException)
    {
        return Results.BadRequest(new { error = "ventana_invalida" });
    }
    catch (System.Data.Common.DbException)
    {
        return Results.Json(
            new { error = "metricas_no_disponibles" },
            statusCode: StatusCodes.Status500InternalServerError);
    }
})
.WithName("ConsultarResumenMetricas")
.RequireAuthorization(Politicas.Administrador, Politicas.PropietarioDelLavadero)
.WithOpenApi();

app.MapGet("/api/lavaderos/{id:guid}/metricas/ocupacion", async (
    Guid id,
    string? fecha,
    Guid? puestoId,
    string? desde,
    string? hasta,
    MetricasServicio servicio,
    CancellationToken ct) =>
{
    if (!TryParseDiaLocal(fecha, out var dia))
    {
        return Results.BadRequest(new { error = "fecha_invalida" });
    }

    DateTime desdeUtc;
    DateTime hastaUtc;

    if (string.IsNullOrWhiteSpace(desde) && string.IsNullOrWhiteSpace(hasta))
    {
        (desdeUtc, hastaUtc) = RangoDelDiaLocal(dia);
    }
    else if (DateTimeOffset.TryParse(
                 desde,
                 System.Globalization.CultureInfo.InvariantCulture,
                 System.Globalization.DateTimeStyles.AssumeUniversal,
                 out var inicio)
             && DateTimeOffset.TryParse(
                 hasta,
                 System.Globalization.CultureInfo.InvariantCulture,
                 System.Globalization.DateTimeStyles.AssumeUniversal,
                 out var fin))
    {
        desdeUtc = inicio.UtcDateTime;
        hastaUtc = fin.UtcDateTime;
    }
    else
    {
        return Results.BadRequest(new { error = "ventana_invalida" });
    }

    try
    {
        var ocupacion = await servicio.ObtenerOcupacionAsync(id, desdeUtc, hastaUtc, puestoId, ct);

        if (puestoId is not null && ocupacion.Puestos.Count == 0)
        {
            return Results.NotFound(new { error = "puesto_no_encontrado" });
        }

        return Results.Ok(OcupacionMetricasAJson(ocupacion));
    }
    catch (VentanaInvalidaException)
    {
        return Results.BadRequest(new { error = "ventana_invalida" });
    }
    catch (System.Data.Common.DbException)
    {
        return Results.Json(
            new { error = "metricas_no_disponibles" },
            statusCode: StatusCodes.Status500InternalServerError);
    }
})
.WithName("ConsultarOcupacionMetricas")
.RequireAuthorization(Politicas.Administrador, Politicas.PropietarioDelLavadero)
.WithOpenApi();

// Ciclo de vida del turno desde la agenda (CU-06, US-A06): finalizar (en_curso),
// cancelar (confirmado, D-04.3) y no_presentado manual (confirmado). Toda
// transición pasa por Turno.TransicionarA; una no permitida da 409
// turno_transicion_invalida. Un turno de otro lavadero es 404 (D-04.7).
app.MapPost("/api/lavaderos/{id:guid}/turnos/{turnoId:guid}/finalizar", async (
    Guid id,
    Guid turnoId,
    AgendaServicio servicio,
    CancellationToken ct) =>
{
    try
    {
        var turno = await servicio.FinalizarAsync(id, turnoId, ct);
        return turno is null
            ? Results.NotFound(new { error = "turno_no_encontrado" })
            : Results.Ok(new { turnoId = turno.Id, estado = EstadoAJson(turno.Estado) });
    }
    catch (TurnoTransicionInvalida ex)
    {
        return Results.Conflict(new { error = "turno_transicion_invalida", detalle = ex.Message });
    }
})
.WithName("FinalizarTurno")
.RequireAuthorization(Politicas.Administrador, Politicas.PropietarioDelLavadero)
.WithOpenApi();

app.MapPost("/api/lavaderos/{id:guid}/turnos/{turnoId:guid}/cancelar", async (
    Guid id,
    Guid turnoId,
    AgendaServicio servicio,
    CancellationToken ct) =>
{
    try
    {
        var turno = await servicio.CancelarAsync(id, turnoId, ct);
        return turno is null
            ? Results.NotFound(new { error = "turno_no_encontrado" })
            : Results.Ok(new { turnoId = turno.Id, estado = EstadoAJson(turno.Estado) });
    }
    catch (TurnoTransicionInvalida ex)
    {
        return Results.Conflict(new { error = "turno_transicion_invalida", detalle = ex.Message });
    }
})
.WithName("CancelarTurno")
.RequireAuthorization(Politicas.Administrador, Politicas.PropietarioDelLavadero)
.WithOpenApi();

app.MapPost("/api/lavaderos/{id:guid}/turnos/{turnoId:guid}/no-presentado", async (
    Guid id,
    Guid turnoId,
    AgendaServicio servicio,
    CancellationToken ct) =>
{
    try
    {
        var turno = await servicio.MarcarNoPresentadoAsync(id, turnoId, ct);
        return turno is null
            ? Results.NotFound(new { error = "turno_no_encontrado" })
            : Results.Ok(new { turnoId = turno.Id, estado = EstadoAJson(turno.Estado) });
    }
    catch (TurnoTransicionInvalida ex)
    {
        return Results.Conflict(new { error = "turno_transicion_invalida", detalle = ex.Message });
    }
})
.WithName("MarcarNoPresentado")
.RequireAuthorization(Politicas.Administrador, Politicas.PropietarioDelLavadero)
.WithOpenApi();

// Login (CU-10, SPECS/slice-05 T-53). Endpoint anónimo: verifica credenciales y
// emite la sesión. El rechazo es siempre 401 con el mismo cuerpo genérico
// (CA-9.4): no distingue usuario inexistente, inactivo ni contraseña errónea.
app.MapPost("/api/auth/login", async (
    SolicitudLoginHttp cuerpo,
    AutenticacionServicio servicio,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(cuerpo.NombreUsuario) ||
        string.IsNullOrWhiteSpace(cuerpo.Contrasena))
    {
        return Results.BadRequest(new { error = "datos_invalidos" });
    }

    try
    {
        var sesion = await servicio.IniciarSesionAsync(
            new SolicitudLogin(cuerpo.NombreUsuario, cuerpo.Contrasena), ct);

        return Results.Ok(new
        {
            token = sesion.Sesion.Token,
            expiraEn = sesion.Sesion.ExpiraEnUtc,
            rol = RolAJson(sesion.Rol),
            lavaderoId = sesion.LavaderoId,
        });
    }
    catch (UsuarioNoEncontradoException)
    {
        return Results.Json(
            new { error = "credenciales_invalidas" },
            statusCode: StatusCodes.Status401Unauthorized);
    }
})
.WithName("IniciarSesion")
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

        try
        {
            var resultado = await servicio.ProcesarAsync(tipo, dataId, ct);
            return Results.Ok(new { resultado = resultado.ToString() });
        }
        catch (ComprobanteNoEmitidoException ex)
        {
            // CU-04: la confirmación no pudo emitir el comprobante. No se
            // persiste ni el pago ni la confirmación (RB-3). Mercado Pago
            // reintentará; el error es de configuración del servidor.
            return Results.Json(
                new { error = "comprobante_no_emitido", detalle = ex.Message },
                statusCode: StatusCodes.Status500InternalServerError);
        }
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

/// <summary>Proyección JSON de un puesto (CU-05, §5.1).</summary>
static object PuestoAJson(Puesto puesto) => new
{
    id = puesto.Id,
    nombre = puesto.Nombre,
    activo = puesto.Activo,
};

/// <summary>Proyección JSON de un turno de agenda (CU-06, §5.1): hora local.</summary>
static object TurnoAgendaAJson(TurnoAgenda turno) => new
{
    turnoId = turno.TurnoId,
    puestoId = turno.PuestoId,
    puesto = turno.Puesto,
    tipoLavado = turno.TipoLavado,
    origen = turno.Origen switch
    {
        OrigenTurno.App => "app",
        _ => "espontanea",
    },
    estado = EstadoAJson(turno.Estado),
    desde = ConversorHorario.ALocal(turno.InicioUtc),
    hasta = ConversorHorario.ALocal(turno.FinUtc),
};

/// <summary>
/// Resuelve el día local del lavadero (o el de hoy si no se pidió fecha).
/// Devuelve <c>false</c> cuando el formato no es <c>yyyy-MM-dd</c>.
/// </summary>
static bool TryParseDiaLocal(string? fecha, out DateOnly dia)
{
    if (string.IsNullOrWhiteSpace(fecha))
    {
        dia = DateOnly.FromDateTime(ConversorHorario.ALocal(DateTime.UtcNow));
        return true;
    }

    return DateOnly.TryParseExact(
        fecha,
        "yyyy-MM-dd",
        System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.None,
        out dia);
}

/// <summary>Límites UTC del día local: [00:00 local, 00:00 local del día siguiente).</summary>
static (DateTime DesdeUtc, DateTime HastaUtc) RangoDelDiaLocal(DateOnly dia)
{
    var inicioLocal = dia.ToDateTime(TimeOnly.MinValue);
    return (ConversorHorario.AUtc(inicioLocal), ConversorHorario.AUtc(inicioLocal.AddDays(1)));
}

/// <summary>Proyección del resumen: tres magnitudes de dinero que no se suman (CA-8.2).</summary>
static object ResumenMetricasAJson(ResumenDiario resumen) => new
{
    fecha = resumen.Fecha.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
    capacidad = new
    {
        puestosTotal = resumen.Capacidad.PuestosTotal,
        puestosActivos = resumen.Capacidad.PuestosActivos,
        puestosOcupados = resumen.Capacidad.PuestosOcupados,
        calculadaEn = resumen.Capacidad.CalculadoEnUtc,
    },
    ingresos = new
    {
        senasCobradas = resumen.Ingresos.SenasCobradas,
        facturadoTotal = resumen.Ingresos.FacturadoTotal,
        facturadoMostrador = resumen.Ingresos.FacturadoMostrador,
        moneda = resumen.Ingresos.Moneda,
    },
    turnos = new
    {
        atendidos = resumen.Turnos.Atendidos,
        espontaneos = resumen.Turnos.Espontaneos,
        noPresentados = resumen.Turnos.NoPresentados,
        cancelaciones = resumen.Turnos.Cancelaciones,
    },
};

/// <summary>Proyección de la ocupación: la tasa siempre viaja con su ventana.</summary>
static object OcupacionMetricasAJson(OcupacionDelDia ocupacion) => new
{
    desde = ocupacion.DesdeUtc,
    hasta = ocupacion.HastaUtc,
    puestos = ocupacion.Puestos.Select(puesto => new
    {
        puestoId = puesto.PuestoId,
        nombre = puesto.Nombre,
        minutosOcupados = puesto.MinutosOcupados,
        minutosVentana = puesto.MinutosVentana,
        tasa = puesto.Tasa,
    }),
};

/// <summary>Rol del usuario en su forma snake_case para la respuesta de login (§5.1).</summary>
static string RolAJson(RolUsuario rol) =>
    rol == RolUsuario.Administrador ? "administrador" : "cliente";

/// <summary>Parsea el filtro de estado de la agenda en su forma snake_case (§5.1).</summary>
static bool TryParseEstado(string valor, out EstadoTurno estado)
{
    switch (valor)
    {
        case "pendiente_pago":
            estado = EstadoTurno.PendientePago;
            return true;
        case "confirmado":
            estado = EstadoTurno.Confirmado;
            return true;
        case "en_curso":
            estado = EstadoTurno.EnCurso;
            return true;
        case "finalizado":
            estado = EstadoTurno.Finalizado;
            return true;
        case "cancelado":
            estado = EstadoTurno.Cancelado;
            return true;
        case "no_presentado":
            estado = EstadoTurno.NoPresentado;
            return true;
        case "expirado":
            estado = EstadoTurno.Expirado;
            return true;
        default:
            estado = default;
            return false;
    }
}

/// <summary>Cuerpo de la solicitud de reserva. `inicio` en hora local del lavadero.</summary>
public sealed record SolicitudReservaHttp(
    Guid PuestoId,
    Guid TipoLavadoId,
    Guid ClienteId,
    string? Inicio);

/// <summary>Cuerpo de la creación de puesto (CU-05).</summary>
public sealed record SolicitudPuestoHttp(string? Nombre);

/// <summary>Cuerpo del login (CU-10); credenciales en claro solo en tránsito.</summary>
public sealed record SolicitudLoginHttp(string? NombreUsuario, string? Contrasena);

/// <summary>Cuerpo de la demanda espontánea. `inicio` en hora local del lavadero.</summary>
public sealed record SolicitudEspontaneaHttp(
    Guid PuestoId,
    Guid TipoLavadoId,
    string? Inicio);

/// <summary>Cuerpo del canje: payload QR (<c>washpoints://comprobante/…</c>) o código de respaldo.</summary>
public sealed record SolicitudValidarComprobanteHttp(string? Comprobante);

/// <summary>Cuerpo de la reconciliación: lote de canjes registrados offline.</summary>
public sealed record SolicitudReconciliacionHttp(List<CanjeOfflineHttp>? Canjes);

/// <summary>Canje offline: hash del comprobante y el instante en que el dispositivo lo validó.</summary>
public sealed record CanjeOfflineHttp(string? Hash, DateTimeOffset? ValidadoEn);

/// <summary>Punto de entrada para los tests de integración (WebApplicationFactory).</summary>
public partial class Program;
