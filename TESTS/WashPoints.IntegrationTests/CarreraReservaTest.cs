using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Puertos;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.IntegrationTests.Infra;
using WashPoints.UnitTests.Fakes;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-20 (CA-S1.9, CA-4.1/4.2, CU-03 A1): carrera global de reservas.
///
/// N clientes reservan el <b>mismo</b> puesto+rango a la vez. La EXCLUDE de 3
/// estados arbitra el <c>pendiente_pago</c>: exactamente una request recibe
/// <c>201</c> y las demás <c>409</c>, sin errores 500. Reemplaza la carrera de
/// confirmación de slice-00 (ahora la carrera se decide al reservar).
///
/// Ver SPECS/slice-01-reserva-sena-mp.md T-20 y T-22.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class CarreraReservaTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private const int Concurrentes = 6;

    private readonly ContenedorPostgres _baseDeDatos;

    public CarreraReservaTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    /// <summary>
    /// Día en que reservan los casos: mañana en hora local. Desde el slice-17 la
    /// reserva valida anticipación mínima y horizonte (D-17.5).
    /// </summary>
    private static readonly DateOnly Dia = DateOnly.FromDateTime(DateTime.Now).AddDays(1);

    private async Task LimpiarTablas() =>
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE pagos, turnos, clientes, puestos, tipos_lavado,
                           horarios_atencion, puestos_tipos_lavado, lavaderos CASCADE;
            """);

    private WashPointsDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options);

    private async Task<(Guid LavaderoId, Guid PuestoId, Guid TipoId)> SembrarAsync()
    {
        await using var contexto = CrearContexto();

        var lavadero = Lavadero.Crear("Lavadero Carrera");
        var puesto = Puesto.Crear(lavadero.Id, "Puesto 1");
        var tipo = TipoLavado.Crear(lavadero.Id, "Exterior", 30, 15000m, 3000m);

        contexto.AddRange(lavadero, puesto, tipo);

        // Sin franjas el lavadero está cerrado y toda reserva responde 422
        // (D-17.2): los casos siembran la jornada del día que reservan.
        contexto.HorariosAtencion.Add(FranjaAtencion.Crear(
            lavadero.Id,
            DiaSemana(Dia),
            new TimeOnly(8, 0),
            new TimeOnly(20, 0),
            DateTime.UtcNow));

        await contexto.SaveChangesAsync();

        return (lavadero.Id, puesto.Id, tipo.Id);
    }

    /// <summary>Día ISO-8601 del calendario de atención (domingo = 7).</summary>
    private static int DiaSemana(DateOnly fecha) =>
        fecha.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)fecha.DayOfWeek;

    /// <summary>Instante local del día de los casos, en formato ISO sin zona.</summary>
    private static string Inicio(int hora, int minuto = 0) =>
        $"{Dia:yyyy-MM-dd}T{hora:00}:{minuto:00}:00";

    private CustomWebApplicationFactory CrearApi(PasarelaPagosFakeSimulada pasarela) =>
        new(_baseDeDatos.CadenaConexion, builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _baseDeDatos.CadenaConexion);
            builder.ConfigureServices(servicios =>
            {
                servicios.RemoveAll<IPasarelaPagos>();
                servicios.AddSingleton<IPasarelaPagos>(pasarela);
            });
        });

    [Fact]
    public async Task SeisReservasConcurrentesDelMismoPuestoDejanUnSoloGanador()
    {
        var (lavaderoId, puestoId, tipoId) = await SembrarAsync();
        var pasarela = new PasarelaPagosFakeSimulada();
        using var api = CrearApi(pasarela);
        using var cliente = api.CreateClient();

        // Cada request reserva con un cliente distinto (clave de idempotencia
        // distinta): la EXCLUDE debe arbitrar el slot, no la unicidad de clave.
        async Task<HttpStatusCode> Reservar()
        {
            var respuesta = await cliente.PostAsJsonAsync(
                $"/api/lavaderos/{lavaderoId}/turnos",
                new
                {
                    puestoId,
                    tipoLavadoId = tipoId,
                    clienteId = Guid.NewGuid(),
                    inicio = Inicio(13),
                });

            return respuesta.StatusCode;
        }

        var codigos = await Task.WhenAll(
            Enumerable.Range(0, Concurrentes).Select(_ => Reservar()));

        Assert.Equal(1, codigos.Count(c => c == HttpStatusCode.Created));
        Assert.Equal(Concurrentes - 1, codigos.Count(c => c == HttpStatusCode.Conflict));
        Assert.DoesNotContain(codigos, c => (int)c >= 500);

        // La fuente de verdad (RB-2): exactamente un pendiente_pago quedó en el
        // puesto para ese rango.
        await using var contexto = CrearContexto();
        var turnosDelPuesto = await contexto.Turnos
            .Where(t => t.PuestoId == puestoId)
            .ToListAsync();

        Assert.Single(turnosDelPuesto);
    }
}
