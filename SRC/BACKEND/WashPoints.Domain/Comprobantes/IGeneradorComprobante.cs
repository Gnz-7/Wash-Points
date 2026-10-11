namespace WashPoints.Domain.Comprobantes;

/// <summary>
/// Puerto generador de comprobantes (ARCHITECTURE §7.1; SPECS/slice-02 §3).
///
/// El dominio no conoce el algoritmo: cualquier implementación debe producir un
/// token de alta entropía y garantizar que las DOS representaciones del
/// comprobante (payload QR y código de respaldo) decodifiquen al MISMO token
/// canónico (CA-S2.3). El hash es la clave de búsqueda del canje; el token en
/// claro nunca se persiste (CA-S2.8).
/// </summary>
public interface IGeneradorComprobante
{
    /// <summary>Prefijo del payload QR; el canje lo usa para distinguirlo del código.</summary>
    const string PrefijoPayloadQr = "washpoints://comprobante/";

    /// <summary>Genera un token nuevo de alta entropía (no enumerable).</summary>
    string GenerarToken();

    /// <summary>Calcula el hash SHA-256 (hex) del token canónico.</summary>
    string CalcularHash(string token);

    /// <summary>Construye el payload QR del token (<c>washpoints://comprobante/&lt;token&gt;</c>).</summary>
    string ConstruirPayloadQr(string token);

    /// <summary>Extrae el token canónico de un payload QR. Lanza si no es válido.</summary>
    string ExtraerTokenDePayloadQr(string payload);

    /// <summary>Construye el código de respaldo legible (Base32 Crockford agrupado).</summary>
    string ConstruirCodigoRespaldo(string token);

    /// <summary>Decodifica un código de respaldo al token canónico. Lanza si no es válido.</summary>
    string DecodificarCodigoRespaldo(string codigo);
}
