namespace WashPoints.Domain.Comprobantes;

/// <summary>
/// Puerto de cifrado simétrico del token (SPECS/slice-02 §3, decisión D1).
///
/// Permite re-servir el comprobante al cliente (US-C08 CA-8.4) sin guardar el
/// token en claro en reposo (ARCHITECTURE §8, CA-S2.8). El dominio no conoce el
/// algoritmo; el canje solo usa el hash, de modo que cambiar el esquema de
/// cifrado no toca el flujo de validación.
/// </summary>
public interface ICifradorComprobante
{
    /// <summary>Cifra el token canónico y devuelve una cadena opaca (base64).</summary>
    string Cifrar(string tokenClaro);

    /// <summary>Descifra el token. Lanza si la clave o la integridad no son válidas.</summary>
    string Descifrar(string tokenCifrado);
}
