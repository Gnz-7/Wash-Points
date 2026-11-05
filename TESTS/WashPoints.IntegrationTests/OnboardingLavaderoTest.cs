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
/// T-134: onboarding del lavadero (SPECS/slice-13 §5.5, D-13.7).
///
/// CA-13.9 — el alta de un administrador siembra, en la misma transacción,
///           1 puesto «Puesto 1» activo y los 2 tipos de lavado por defecto
///           (básico 30 min, completo 60 min) activos.
/// CA-13.10 — el lavadero recién creado puede recibir una reserva real sin
///            ninguna inserción manual previa: el catálogo alcanza.
///
/// El test no crea lavadero, puesto ni tipo por SQL: todo sale del alta HTTP,
/// que es el escenario del alta real.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class OnboardingLavaderoTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private readonly ContenedorPostgres _baseDeDatos;

    public OnboardingLavaderoTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private async Task LimpiarTablas() =>
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE outbox, comprobantes, pagos, turnos, usuarios,
                           clientes, puestos, tipos_lavado, lavaderos CASCADE;
            """);

    private CustomWebApplicationFactory CrearApi() =>
        new(_baseDeDatos.CadenaConexion, builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _baseDeDatos.CadenaConexion);
            builder.ConfigureServices(servicios =>
            {
                servicios.RemoveAll<IPasarelaPagos>();
                servicios.AddSingleton<IPasarelaPagos>(new PasarelaPagosFakeSimulada());
            });
        });

    private static object CuerpoAdministrador(string usuario) => new
    {
        nombre = "Ana Pérez",
        usuario,
        contrasena = "unaClaveSegura1",
        nombreLavadero = "Lavadero Central",
        direccion = "Av. Rivadavia 1234",
        latitud = -34.6037,
        longitud = -58.3816,
    };

    private static async Task<RespuestaRegistro> RegistrarAdministradorAsync(
        HttpClient cliente,
        string usuario = "ana.perez")
    {
        var respuesta = await cliente.PostAsJsonAsync(
            "/api/registro/administrador", CuerpoAdministrador(usuario));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);

        var registro = await respuesta.Content.ReadFromJsonAsync<RespuestaRegistro>();
        Assert.NotNull(registro);
        Assert.NotNull(registro!.lavaderoId);
        return registro!;
    }

    [Fact]
    public async Task ElAltaDelAdministradorSiembraUnPuestoYLosDosTiposPorDefecto()
    {
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var registro = await RegistrarAdministradorAsync(cliente);
        var lavaderoId = registro.lavaderoId!.Value;

        var puestos = await cliente.GetFromJsonAsync<List<RespuestaPuesto>>(
            $"/api/lavaderos/{lavaderoId}/puestos");
        Assert.NotNull(puestos);
        var puesto = Assert.Single(puestos!);
        Assert.Equal("Puesto 1", puesto.nombre);
        Assert.True(puesto.activo);

        var tipos = await cliente.GetFromJsonAsync<List<RespuestaTipo>>(
            $"/api/lavaderos/{lavaderoId}/tipos-lavado");
        Assert.NotNull(tipos);
        Assert.Equal(2, tipos!.Count);

        var basico = tipos.Single(t => t.nombre == "Lavado básico");
        Assert.Equal(30, basico.duracionMinutos);
        Assert.Equal(8000m, basico.precioTotal);
        Assert.Equal(1500m, basico.precioSena);
        Assert.True(basico.activo);

        var completo = tipos.Single(t => t.nombre == "Lavado completo");
        Assert.Equal(60, completo.duracionMinutos);
        Assert.Equal(15000m, completo.precioTotal);
        Assert.Equal(2500m, completo.precioSena);
        Assert.True(completo.activo);
    }

    [Fact]
    public async Task UnLavaderoReciénCreadoPuedeReservarSinConfigurarNada()
    {
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var administrador = await RegistrarAdministradorAsync(cliente);
        var lavaderoId = administrador.lavaderoId!.Value;

        var registroCliente = await cliente.PostAsJsonAsync(
            "/api/registro/cliente",
            new
            {
                nombre = "Juan Gómez",
                usuario = "juan.gomez",
                contrasena = "otraClaveSegura1",
            });
        Assert.Equal(HttpStatusCode.Created, registroCliente.StatusCode);

        var clienteNuevo = await registroCliente.Content.ReadFromJsonAsync<RespuestaRegistro>();
        Assert.NotNull(clienteNuevo);
        Assert.NotNull(clienteNuevo!.clienteId);

        var puesto = Assert.Single(
            (await cliente.GetFromJsonAsync<List<RespuestaPuesto>>(
                $"/api/lavaderos/{lavaderoId}/puestos"))!);

        var tipo = Assert.Single(
            (await cliente.GetFromJsonAsync<List<RespuestaTipo>>(
                $"/api/lavaderos/{lavaderoId}/tipos-lavado"))!,
            t => t.nombre == "Lavado básico");

        var inicioLocal = DateTime.Now.Date.AddDays(1).AddHours(10)
            .ToString("yyyy-MM-ddTHH:mm:ss");

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/turnos",
            new
            {
                puestoId = puesto.id,
                tipoLavadoId = tipo.id,
                clienteId = clienteNuevo.clienteId!.Value,
                inicio = inicioLocal,
            });

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);

        var reserva = await respuesta.Content.ReadFromJsonAsync<RespuestaReserva>();
        Assert.NotNull(reserva);
        Assert.Equal("pendiente_pago", reserva!.estado);
        Assert.False(string.IsNullOrWhiteSpace(reserva.mpPreferenceId));
    }

    private sealed record RespuestaRegistro(
        Guid usuarioId,
        Guid? clienteId,
        Guid? lavaderoId,
        string rol,
        string token,
        DateTime expiraEn);

    private sealed record RespuestaPuesto(Guid id, string nombre, bool activo);

    private sealed record RespuestaTipo(
        Guid id,
        string nombre,
        int duracionMinutos,
        decimal precioTotal,
        decimal precioSena,
        bool activo);

    private sealed record RespuestaReserva(Guid id, string estado, string? mpPreferenceId);
}
