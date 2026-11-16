namespace WashPoints.Domain.MercadoPago;

/// <summary>
/// Puerto de cifrado de los tokens de Mercado Pago de cada lavadero
/// (SPECS/slice-20, D-20.3/D-20.8).
///
/// Los tokens de acceso y refresco se guardan cifrados en reposo
/// (CA-S20.12); el perfil y ningún endpoint los devuelven jamás (AGENTS §3.4).
/// La clave va por configuración (<c>MercadoPago:ClaveTokens</c>, 32 bytes en
/// base64), nunca en el repositorio. Sin clave, la vinculación falla de forma
/// explícita (CA-S20.6), nunca degradada.
/// </summary>
public interface ICifradorTokensMp
{
    /// <summary>Indica si hay una clave configurada. Si no, la vinculación falla explícitamente.</summary>
    bool EstaConfigurado { get; }

    /// <summary>Cifra el token en claro y devuelve una cadena opaca (base64).</summary>
    string Cifrar(string tokenClaro);

    /// <summary>Descifra el token. Lanza si la clave o la integridad no son válidas.</summary>
    string Descifrar(string tokenCifrado);
}
