using WashPoints.Application.Catalogo;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Seguridad;

namespace WashPoints.Application.Registro;

/// <summary>
/// Alta de cuentas (slice-08, D-09.2/D-09.3).
///
/// Cada alta corre dentro de un único bloque transaccional de
/// <see cref="IUnidadDeTrabajo"/>: si la credencial no se puede crear, no
/// queda ni lavadero ni cliente huérfano (§4.3). La contraseña se hashea con
/// PBKDF2 antes de tocar la persistencia y el token se emite recién después de
/// persistir: un alta revertida no entrega sesión.
/// </summary>
public sealed class RegistroServicio
{
    /// <summary>Mínimo de caracteres de la contraseña (D-09.4).</summary>
    public const int LongitudMinimaContrasena = 8;

    /// <summary>Mínimo de caracteres del nombre de usuario.</summary>
    public const int LongitudMinimaUsuario = 3;

    /// <summary>Máximo de caracteres del nombre de usuario.</summary>
    public const int LongitudMaximaUsuario = 50;

    private readonly IClienteRepositorio _clientes;
    private readonly ILavaderoRepositorio _lavaderos;
    private readonly IUsuarioRepositorio _usuarios;
    private readonly IHasheadorContrasena _hasheador;
    private readonly IEmisorToken _emisor;
    private readonly IUnidadDeTrabajo _unidadDeTrabajo;
    private readonly IPuestoRepositorio _puestos;
    private readonly ITipoLavadoRepositorio _tipos;
    private readonly OpcionesCatalogo _opciones;

    public RegistroServicio(
        IClienteRepositorio clientes,
        ILavaderoRepositorio lavaderos,
        IUsuarioRepositorio usuarios,
        IHasheadorContrasena hasheador,
        IEmisorToken emisor,
        IUnidadDeTrabajo unidadDeTrabajo,
        IPuestoRepositorio puestos,
        ITipoLavadoRepositorio tipos,
        OpcionesCatalogo opciones)
    {
        _clientes = clientes ?? throw new ArgumentNullException(nameof(clientes));
        _lavaderos = lavaderos ?? throw new ArgumentNullException(nameof(lavaderos));
        _usuarios = usuarios ?? throw new ArgumentNullException(nameof(usuarios));
        _hasheador = hasheador ?? throw new ArgumentNullException(nameof(hasheador));
        _emisor = emisor ?? throw new ArgumentNullException(nameof(emisor));
        _unidadDeTrabajo = unidadDeTrabajo ?? throw new ArgumentNullException(nameof(unidadDeTrabajo));
        _puestos = puestos ?? throw new ArgumentNullException(nameof(puestos));
        _tipos = tipos ?? throw new ArgumentNullException(nameof(tipos));
        _opciones = opciones ?? throw new ArgumentNullException(nameof(opciones));
    }

    /// <summary>Alta de un cliente conductor con sesión inmediata (D-09.2).</summary>
    public async Task<RegistroSesion> RegistrarClienteAsync(
        ClienteNuevo datos,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        ValidarCredencial(datos.Nombre, datos.Usuario, datos.Contrasena);

        return await _unidadDeTrabajo.EjecutarEnTransaccionAsync(async token =>
        {
            await AsegurarUsuarioLibreAsync(datos.Usuario, token);

            var cliente = Cliente.Crear(datos.Nombre);
            await _clientes.AgregarAsync(cliente, token);

            var usuario = Usuario.Crear(
                datos.Usuario,
                _hasheador.Hashear(datos.Contrasena),
                RolUsuario.Cliente,
                clienteId: cliente.Id);

            await _usuarios.AgregarAsync(usuario, token);

            var sesion = _emisor.Emitir(usuario.Id, RolUsuario.Cliente, null, cliente.Id);

            return new RegistroSesion(usuario.Id, cliente.Id, null, RolUsuario.Cliente, sesion);
        }, ct);
    }

    /// <summary>
    /// Alta de un administrador y de su lavadero vacío, en una sola transacción
    /// (D-09.3, §4.3).
    /// </summary>
    public async Task<RegistroSesion> RegistrarAdministradorAsync(
        AdministradorNuevo datos,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        ValidarCredencial(datos.Nombre, datos.Usuario, datos.Contrasena);

        if (string.IsNullOrWhiteSpace(datos.NombreLavadero))
        {
            throw new LavaderoInvalidoException("El nombre del lavadero es obligatorio.");
        }

        if (datos.Latitud is not { } latitud || datos.Longitud is not { } longitud)
        {
            throw new UbicacionInvalidaException(
                "La ubicación del lavadero es obligatoria: latitud y longitud.");
        }

        if (latitud is < -90 or > 90 || longitud is < -180 or > 180)
        {
            throw new UbicacionInvalidaException(
                "La ubicación del lavadero está fuera del rango admitido.");
        }

        return await _unidadDeTrabajo.EjecutarEnTransaccionAsync(async token =>
        {
            await AsegurarUsuarioLibreAsync(datos.Usuario, token);

            var lavadero = Lavadero.Crear(datos.NombreLavadero);
            lavadero.Ubicar(datos.Direccion, latitud, longitud);
            await _lavaderos.AgregarAsync(lavadero, token);

            var usuario = Usuario.Crear(
                datos.Usuario,
                _hasheador.Hashear(datos.Contrasena),
                RolUsuario.Administrador,
                lavaderoId: lavadero.Id);

            await _usuarios.AgregarAsync(usuario, token);

            // Onboarding del lavadero (SPECS/slice-13 §5.5, D-13.7): un puesto
            // base y los dos tipos de lavado por defecto, dentro de la MISMA
            // transacción del alta. Si algo falla, no queda lavadero a medio
            // configurar. El flush lo hace el repositorio de tipos: persiste
            // puesto + tipos en un solo SaveChanges.
            await _puestos.AgregarAsync(Puesto.Crear(lavadero.Id, "Puesto 1"), token);

            await _tipos.AgregarAsync(
                TipoLavado.Crear(
                    lavadero.Id,
                    "Lavado básico",
                    30,
                    _opciones.LavadoBasico.PrecioTotal,
                    _opciones.LavadoBasico.PrecioSena),
                token);

            await _tipos.AgregarAsync(
                TipoLavado.Crear(
                    lavadero.Id,
                    "Lavado completo",
                    60,
                    _opciones.LavadoCompleto.PrecioTotal,
                    _opciones.LavadoCompleto.PrecioSena),
                token);

            await _tipos.GuardarAsync(token);

            var sesion = _emisor.Emitir(usuario.Id, RolUsuario.Administrador, lavadero.Id);

            return new RegistroSesion(usuario.Id, null, lavadero.Id, RolUsuario.Administrador, sesion);
        }, ct);
    }

    private async Task AsegurarUsuarioLibreAsync(string usuario, CancellationToken ct)
    {
        if (await _usuarios.ExisteAsync(usuario.Trim(), ct))
        {
            throw new UsuarioYaExisteException("Ya existe una cuenta con ese nombre de usuario.");
        }
    }

    private static void ValidarCredencial(string nombre, string usuario, string contrasena)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            throw new NombreInvalidoException("El nombre es obligatorio.");
        }

        var identificador = usuario?.Trim() ?? string.Empty;

        if (identificador.Length < LongitudMinimaUsuario ||
            identificador.Length > LongitudMaximaUsuario ||
            identificador.Any(char.IsWhiteSpace))
        {
            throw new UsuarioInvalidoException(
                $"El nombre de usuario debe tener entre {LongitudMinimaUsuario} y " +
                $"{LongitudMaximaUsuario} caracteres y no admite espacios.");
        }

        if (string.IsNullOrWhiteSpace(contrasena) ||
            contrasena.Length < LongitudMinimaContrasena)
        {
            throw new ContrasenaInvalidaException(
                $"La contraseña debe tener al menos {LongitudMinimaContrasena} caracteres.");
        }
    }
}
