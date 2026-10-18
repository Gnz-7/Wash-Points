using WashPoints.Domain.Seguridad;

namespace WashPoints.Application.Seguridad;

/// <summary>
/// Login (CU-10, SPECS/slice-05 T-53). Verifica credenciales y emite la sesión.
///
/// El rechazo es siempre el mismo (D-05.5, CA-9.4): un usuario inexistente, uno
/// inactivo y una contraseña errónea lanzan <see cref="UsuarioNoEncontradoException"/>
/// sin distinguir el motivo. La comparación del hash es de tiempo constante
/// (<see cref="IHasheadorContrasena"/> la implementa así).
/// </summary>
public sealed class AutenticacionServicio
{
    private readonly IUsuarioRepositorio _usuarios;
    private readonly IHasheadorContrasena _hasheador;
    private readonly IEmisorToken _emisor;

    public AutenticacionServicio(
        IUsuarioRepositorio usuarios,
        IHasheadorContrasena hasheador,
        IEmisorToken emisor)
    {
        _usuarios = usuarios;
        _hasheador = hasheador;
        _emisor = emisor;
    }

    public async Task<SesionIniciada> IniciarSesionAsync(
        SolicitudLogin solicitud, CancellationToken ct)
    {
        var usuario = await _usuarios.BuscarPorNombreAsync(solicitud.NombreUsuario, ct);

        if (usuario is null ||
            !usuario.Activo ||
            !_hasheador.Verificar(solicitud.Contrasena, usuario.HashContrasena))
        {
            throw new UsuarioNoEncontradoException("Credenciales inválidas.");
        }

        var sesion = _emisor.Emitir(usuario.Id, usuario.Rol, usuario.LavaderoId, usuario.ClienteId);

        return new SesionIniciada(sesion, usuario.Rol, usuario.LavaderoId);
    }
}
