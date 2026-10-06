using Microsoft.EntityFrameworkCore;
using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Turnos;
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
// huso. Solo aparecen turnos que ocupan puesto (confirmado/en_curso): la
// consulta los filtra (RB-2).
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

app.Run();

/// <summary>Punto de entrada para los tests de integración (WebApplicationFactory).</summary>
public partial class Program;