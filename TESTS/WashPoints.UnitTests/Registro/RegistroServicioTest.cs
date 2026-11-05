using WashPoints.Application.Catalogo;
using WashPoints.Application.Registro;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Seguridad;
using Xunit;

namespace WashPoints.UnitTests.Registro;

/// <summary>
/// T-82 (rojo). Fija el contrato de <c>RegistroServicio</c> (CU de cuentas,
/// slice-08, D-09.1…D-09.9): validaciones de campo, hash de la contraseña,
/// alta de cliente con <c>clienteId</c>, alta de administrador atómica dentro
/// de <c>IUnidadDeTrabajo</c> con <c>lavaderoId</c> y propagación del
/// conflicto de unicidad.
/// </summary>
public sealed class RegistroServicioTest
{
    private const string Nombre = "Ana Pérez";
    private const string Usuario = "ana.perez";
    private const string Contrasena = "unaClaveSegura1";
    private const string NombreLavadero = "Lavadero Centro";
    private const double Latitud = -34.6037;
    private const double Longitud = -58.3816;

    [Fact]
    public async Task UnNombreVacioSeRechaza()
    {
        var (servicio, _, _, _, _, _, _) = Crear();

        await Assert.ThrowsAsync<NombreInvalidoException>(() =>
            servicio.RegistrarClienteAsync(new ClienteNuevo("   ", Usuario, Contrasena)));
    }

    [Fact]
    public async Task UnUsuarioConEspaciosSeRechaza()
    {
        var (servicio, _, _, _, _, _, _) = Crear();

        await Assert.ThrowsAsync<UsuarioInvalidoException>(() =>
            servicio.RegistrarClienteAsync(new ClienteNuevo(Nombre, "ana perez", Contrasena)));
    }

    [Fact]
    public async Task UnaContrasenaCortaSeRechaza()
    {
        var (servicio, _, _, _, _, _, _) = Crear();

        await Assert.ThrowsAsync<ContrasenaInvalidaException>(() =>
            servicio.RegistrarClienteAsync(new ClienteNuevo(Nombre, Usuario, "corta")));
    }

    [Fact]
    public async Task UnNombreDeLavaderoVacioSeRechaza()
    {
        var (servicio, _, _, _, _, _, _) = Crear();

        await Assert.ThrowsAsync<LavaderoInvalidoException>(() =>
            servicio.RegistrarAdministradorAsync(Admin(nombreLavadero: "  ")));
    }

    [Fact]
    public async Task UnaUbicacionFaltanteSeRechaza()
    {
        var (servicio, _, _, _, _, _, _) = Crear();

        await Assert.ThrowsAsync<UbicacionInvalidaException>(() =>
            servicio.RegistrarAdministradorAsync(Admin(latitud: null)));
    }

    [Fact]
    public async Task LaContrasenaSeGuardaHasheada()
    {
        var (servicio, _, _, usuarios, hasheador, _, _) = Crear();

        await servicio.RegistrarClienteAsync(new ClienteNuevo(Nombre, Usuario, Contrasena));

        var usuario = Assert.Single(usuarios.Agregados);
        Assert.Equal(hasheador.Hashear(Contrasena), usuario.HashContrasena);
        Assert.NotEqual(Contrasena, usuario.HashContrasena);
    }

    [Fact]
    public async Task ElClienteSeCreaYDevuelveSuClienteId()
    {
        var (servicio, clientes, _, usuarios, _, _, _) = Crear();

        var registro = await servicio.RegistrarClienteAsync(
            new ClienteNuevo(Nombre, Usuario, Contrasena));

        var cliente = Assert.Single(clientes.Agregados);
        Assert.Equal(cliente.Id, registro.ClienteId);
        Assert.Null(registro.LavaderoId);
        Assert.Equal(RolUsuario.Cliente, registro.Rol);
        Assert.Equal(Assert.Single(usuarios.Agregados).Id, registro.UsuarioId);
    }

    [Fact]
    public async Task ElAdministradorSeCreaDentroDeUnaTransaccion()
    {
        var (servicio, _, lavaderos, usuarios, _, _, unidad) = Crear();

        await servicio.RegistrarAdministradorAsync(Admin());

        Assert.Equal(1, unidad.Ejecuciones);
        var lavadero = Assert.Single(lavaderos.Agregados);
        Assert.Equal(lavadero.Id, Assert.Single(usuarios.Agregados).LavaderoId);
    }

    [Fact]
    public async Task ElAdministradorQuedaVinculadoASuLavadero()
    {
        var (servicio, _, lavaderos, usuarios, _, _, _) = Crear();

        var registro = await servicio.RegistrarAdministradorAsync(Admin());

        var lavadero = Assert.Single(lavaderos.Agregados);
        Assert.Equal(lavadero.Id, registro.LavaderoId);
        Assert.Null(registro.ClienteId);
        Assert.Equal(RolUsuario.Administrador, registro.Rol);
        Assert.Null(Assert.Single(usuarios.Agregados).ClienteId);
    }

    [Fact]
    public async Task ElTokenSeEmiteConElRolYElCliente()
    {
        var (servicio, _, _, _, _, emisor, _) = Crear();

        var registro = await servicio.RegistrarClienteAsync(
            new ClienteNuevo(Nombre, Usuario, Contrasena));

        Assert.Equal(registro.UsuarioId, emisor.UsuarioId);
        Assert.Equal(RolUsuario.Cliente, emisor.Rol);
        Assert.Equal(registro.ClienteId, emisor.ClienteId);
        Assert.Null(emisor.LavaderoId);
        Assert.Equal("token-de-prueba", registro.Sesion.Token);
    }

    [Fact]
    public async Task UnUsuarioDuplicadoSePropaga()
    {
        var (servicio, _, _, usuarios, _, _, _) = Crear();
        usuarios.Error = new UsuarioYaExisteException("El nombre de usuario ya está tomado.");

        await Assert.ThrowsAsync<UsuarioYaExisteException>(() =>
            servicio.RegistrarClienteAsync(new ClienteNuevo(Nombre, Usuario, Contrasena)));
    }

    private static AdministradorNuevo Admin(
        string nombreLavadero = NombreLavadero,
        double? latitud = Latitud)
        => new(Nombre, Usuario, Contrasena, nombreLavadero, "Av. Rivadavia 1234", latitud, Longitud);

    private static (
        RegistroServicio Servicio,
        ClienteRepositorioFake Clientes,
        LavaderoRepositorioFake Lavaderos,
        UsuarioRepositorioFake Usuarios,
        HasheadorFake Hasheador,
        EmisorTokenFake Emisor,
        UnidadDeTrabajoFake Unidad) Crear()
    {
        var clientes = new ClienteRepositorioFake();
        var lavaderos = new LavaderoRepositorioFake();
        var usuarios = new UsuarioRepositorioFake();
        var hasheador = new HasheadorFake();
        var emisor = new EmisorTokenFake();
        var unidad = new UnidadDeTrabajoFake();

        var servicio = new RegistroServicio(
            clientes, lavaderos, usuarios, hasheador, emisor, unidad,
            new PuestoRepositorioFake(), new TipoLavadoRepositorioFake(),
            new OpcionesCatalogo());

        return (servicio, clientes, lavaderos, usuarios, hasheador, emisor, unidad);
    }

    /// <summary>
    /// Variante que expone el catálogo sembrado por el onboarding (T-134) y
    /// permite inyectar otras opciones de precios.
    /// </summary>
    private static (
        RegistroServicio Servicio,
        PuestoRepositorioFake Puestos,
        TipoLavadoRepositorioFake Tipos,
        UnidadDeTrabajoFake Unidad) CrearConCatalogo(OpcionesCatalogo? opciones = null)
    {
        var puestos = new PuestoRepositorioFake();
        var tipos = new TipoLavadoRepositorioFake();
        var unidad = new UnidadDeTrabajoFake();

        var servicio = new RegistroServicio(
            new ClienteRepositorioFake(),
            new LavaderoRepositorioFake(),
            new UsuarioRepositorioFake(),
            new HasheadorFake(),
            new EmisorTokenFake(),
            unidad,
            puestos,
            tipos,
            opciones ?? new OpcionesCatalogo());

        return (servicio, puestos, tipos, unidad);
    }

    [Fact]
    public async Task ElAltaDelAdministradorSiembraUnPuestoYLosDosTipos()
    {
        var (servicio, puestos, tipos, _) = CrearConCatalogo();

        var registro = await servicio.RegistrarAdministradorAsync(Admin());

        var puesto = Assert.Single(puestos.Agregados);
        Assert.Equal("Puesto 1", puesto.Nombre);
        Assert.True(puesto.Activo);
        Assert.Equal(registro.LavaderoId, puesto.LavaderoId);

        Assert.Collection(
            tipos.Agregados,
            basico =>
            {
                Assert.Equal("Lavado básico", basico.Nombre);
                Assert.Equal(30, basico.DuracionMinutos);
                Assert.Equal(8000m, basico.PrecioTotal);
                Assert.Equal(1500m, basico.PrecioSena);
                Assert.True(basico.Activo);
                Assert.Equal(registro.LavaderoId, basico.LavaderoId);
            },
            completo =>
            {
                Assert.Equal("Lavado completo", completo.Nombre);
                Assert.Equal(60, completo.DuracionMinutos);
                Assert.Equal(15000m, completo.PrecioTotal);
                Assert.Equal(2500m, completo.PrecioSena);
                Assert.True(completo.Activo);
                Assert.Equal(registro.LavaderoId, completo.LavaderoId);
            });

        Assert.Equal(1, tipos.Guardadas);
    }

    [Fact]
    public async Task ElOnboardingUsaLosPreciosConfigurados()
    {
        var opciones = new OpcionesCatalogo
        {
            LavadoBasico = new PrecioTipoLavado { PrecioTotal = 9000m, PrecioSena = 2000m },
            LavadoCompleto = new PrecioTipoLavado { PrecioTotal = 20000m, PrecioSena = 4000m },
        };
        var (servicio, _, tipos, _) = CrearConCatalogo(opciones);

        await servicio.RegistrarAdministradorAsync(Admin());

        Assert.Equal(9000m, tipos.Agregados[0].PrecioTotal);
        Assert.Equal(2000m, tipos.Agregados[0].PrecioSena);
        Assert.Equal(20000m, tipos.Agregados[1].PrecioTotal);
        Assert.Equal(4000m, tipos.Agregados[1].PrecioSena);
    }

    [Fact]
    public async Task ElAltaDeUnClienteNoSiembraCatalogo()
    {
        var (servicio, puestos, tipos, _) = CrearConCatalogo();

        await servicio.RegistrarClienteAsync(new ClienteNuevo(Nombre, Usuario, Contrasena));

        Assert.Empty(puestos.Agregados);
        Assert.Empty(tipos.Agregados);
    }

    private sealed class PuestoRepositorioFake : IPuestoRepositorio
    {
        public List<Puesto> Agregados { get; } = new();

        public Task AgregarAsync(Puesto puesto, CancellationToken ct = default)
        {
            Agregados.Add(puesto);
            return Task.CompletedTask;
        }

        public Task<Puesto?> ObtenerAsync(Guid lavaderoId, Guid puestoId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Puesto>> ListarAsync(Guid lavaderoId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task GuardarAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class TipoLavadoRepositorioFake : ITipoLavadoRepositorio
    {
        public List<TipoLavado> Agregados { get; } = new();

        public int Guardadas { get; private set; }

        public Task AgregarAsync(TipoLavado tipo, CancellationToken ct = default)
        {
            Agregados.Add(tipo);
            return Task.CompletedTask;
        }

        public Task<TipoLavado?> ObtenerAsync(Guid lavaderoId, Guid tipoId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TipoLavado>> ListarAsync(Guid lavaderoId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task GuardarAsync(CancellationToken ct = default)
        {
            Guardadas++;
            return Task.CompletedTask;
        }
    }

    private sealed class ClienteRepositorioFake : IClienteRepositorio
    {
        public List<Cliente> Agregados { get; } = new();

        public Task AgregarAsync(Cliente cliente, CancellationToken ct = default)
        {
            Agregados.Add(cliente);
            return Task.CompletedTask;
        }
    }

    private sealed class LavaderoRepositorioFake : ILavaderoRepositorio
    {
        public List<Lavadero> Agregados { get; } = new();

        public Task AgregarAsync(Lavadero lavadero, CancellationToken ct = default)
        {
            Agregados.Add(lavadero);
            return Task.CompletedTask;
        }
    }

    private sealed class UsuarioRepositorioFake : IUsuarioRepositorio
    {
        public List<Usuario> Agregados { get; } = new();

        public Exception? Error { get; set; }

        public Task<Usuario?> BuscarPorNombreAsync(
            string nombreUsuario,
            CancellationToken ct = default)
            => Task.FromResult<Usuario?>(null);

        public Task<bool> ExisteAsync(string nombreUsuario, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task AgregarAsync(Usuario usuario, CancellationToken ct = default)
        {
            if (Error is not null)
            {
                return Task.FromException(Error);
            }

            Agregados.Add(usuario);
            return Task.CompletedTask;
        }
    }

    private sealed class HasheadorFake : IHasheadorContrasena
    {
        public string Hashear(string contrasena) => "hash:" + contrasena;

        public bool Verificar(string contrasena, string hash) => hash == "hash:" + contrasena;
    }

    private sealed class EmisorTokenFake : IEmisorToken
    {
        public Guid UsuarioId { get; private set; }

        public RolUsuario Rol { get; private set; }

        public Guid? LavaderoId { get; private set; }

        public Guid? ClienteId { get; private set; }

        public SesionEmitida Emitir(
            Guid usuarioId,
            RolUsuario rol,
            Guid? lavaderoId,
            Guid? clienteId = null)
        {
            UsuarioId = usuarioId;
            Rol = rol;
            LavaderoId = lavaderoId;
            ClienteId = clienteId;

            return new SesionEmitida("token-de-prueba", DateTime.UtcNow.AddHours(1));
        }
    }

    private sealed class UnidadDeTrabajoFake : IUnidadDeTrabajo
    {
        public int Ejecuciones { get; private set; }

        public async Task<T> EjecutarEnTransaccionAsync<T>(
            Func<CancellationToken, Task<T>> trabajo,
            CancellationToken ct)
        {
            Ejecuciones++;
            return await trabajo(ct);
        }
    }
}
