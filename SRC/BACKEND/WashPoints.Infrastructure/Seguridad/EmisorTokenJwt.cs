using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using WashPoints.Domain.Seguridad;

namespace WashPoints.Infrastructure.Seguridad;

/// <summary>
/// Emisor de sesiones JWT firmadas con HS256 (D-05.2). El token transporta
/// <c>sub</c> (id del usuario de la tabla <c>usuarios</c>), <c>rol</c> y, para
/// administradores, <c>lavaderoId</c>.
/// </summary>
public sealed class EmisorTokenJwt : IEmisorToken
{
    /// <summary>HS256 exige una clave de al menos 256 bits (32 caracteres).</summary>
    private const int LargoMinimoClave = 32;

    private readonly string _emisor;
    private readonly string _audiencia;
    private readonly int _minutosVida;
    private readonly TimeProvider _reloj;
    private readonly SigningCredentials _credenciales;
    private readonly JwtSecurityTokenHandler _manejador = new();

    public EmisorTokenJwt(
        string claveSecreta,
        string emisor,
        string audiencia,
        int minutosVida,
        TimeProvider reloj)
    {
        if (string.IsNullOrWhiteSpace(claveSecreta) || claveSecreta.Length < LargoMinimoClave)
        {
            throw new ArgumentException(
                $"La clave de firma (Jwt:ClaveSecreta) debe tener al menos {LargoMinimoClave} caracteres.",
                nameof(claveSecreta));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(emisor);
        ArgumentException.ThrowIfNullOrWhiteSpace(audiencia);
        ArgumentNullException.ThrowIfNull(reloj);

        _emisor = emisor;
        _audiencia = audiencia;
        _minutosVida = minutosVida;
        _reloj = reloj;

        var clave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(claveSecreta));
        _credenciales = new SigningCredentials(clave, SecurityAlgorithms.HmacSha256);
    }

    /// <inheritdoc />
    public SesionEmitida Emitir(
        Guid usuarioId,
        RolUsuario rol,
        Guid? lavaderoId,
        Guid? clienteId = null)
    {
        var ahora = _reloj.GetUtcNow().UtcDateTime;
        var expira = ahora.AddMinutes(_minutosVida);

        var claims = new List<Claim>
        {
            new(ClaimsWashPoints.Sub, usuarioId.ToString()),
            new(ClaimsWashPoints.Rol, rol == RolUsuario.Administrador
                ? ClaimsWashPoints.Administrador
                : ClaimsWashPoints.Cliente),
        };

        if (lavaderoId is { } id)
        {
            claims.Add(new Claim(ClaimsWashPoints.LavaderoId, id.ToString()));
        }

        // D-06.2: el vínculo con el cliente de negocio viaja solo si existe.
        if (clienteId is { } idCliente)
        {
            claims.Add(new Claim(ClaimsWashPoints.ClienteId, idCliente.ToString()));
        }

        var token = new JwtSecurityToken(
            issuer: _emisor,
            audience: _audiencia,
            claims: claims,
            notBefore: ahora,
            expires: expira,
            signingCredentials: _credenciales);

        return new SesionEmitida(_manejador.WriteToken(token), expira);
    }
}
