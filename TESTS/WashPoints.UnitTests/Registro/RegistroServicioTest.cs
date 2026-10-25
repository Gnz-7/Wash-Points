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
            clientes, lavaderos, usuarios, hasheador, emisor, unidad);

        return (servicio, clientes, lavaderos, usuarios, hasheador, emisor, unidad);
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
