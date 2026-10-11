using WashPoints.Domain.Comprobantes;
using WashPoints.Infrastructure.Persistencia;

namespace WashPoints.Infrastructure.Comprobantes;

/// <summary>
/// Emisión del comprobante (SPECS/slice-02 T-26, RB-3).
///
/// Construye el token (CSPRNG), su hash SHA-256 y su copia cifrada, y <b>agrega
/// la entidad al contexto compartido</b>: el comprobante se persiste en el mismo
/// <c>SaveChangesAsync</c> que confirmó el turno, de modo que no puede existir un
/// <c>confirmado</c> sin comprobante. Es idempotente por el
/// <c>UNIQUE(turno_id)</c> de la tabla: un segundo intento no crea una fila.
///
/// Vive en Infrastructure (no en Application) para mantener la regla de
/// dependencia <c>Infrastructure → Domain</c>: la confirmación (`Pagos`) la
/// invoca a través del puerto <see cref="IEmisorComprobante"/>.
/// </summary>
public sealed class EmisorComprobantes : IEmisorComprobante
{
    private readonly IGeneradorComprobante _generador;
    private readonly ICifradorComprobante _cifrador;
    private readonly WashPointsDbContext _contexto;
    private readonly int _ttlVigenciaHoras;

    public EmisorComprobantes(
        IGeneradorComprobante generador,
        ICifradorComprobante cifrador,
        WashPointsDbContext contexto,
        int ttlVigenciaHoras)
    {
        _generador = generador;
        _cifrador = cifrador;
        _contexto = contexto;
        _ttlVigenciaHoras = ttlVigenciaHoras;
    }

    public void Emitir(Guid turnoId, DateTime confirmadoEnUtc)
    {
        if (confirmadoEnUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "La confirmación del turno debe estar en UTC.", nameof(confirmadoEnUtc));
        }

        if (_ttlVigenciaHoras <= 0)
        {
            throw new ComprobanteNoEmitidoException(
                "El TTL de vigencia del comprobante no es válido (Comprobantes:TtlVigenciaHoras).");
        }

        try
        {
            var token = _generador.GenerarToken();
            var hash = _generador.CalcularHash(token);
            var tokenCifrado = _cifrador.Cifrar(token);

            var comprobante = Comprobante.Emitir(
                turnoId,
                hash,
                tokenCifrado,
                confirmadoEnUtc.AddHours(_ttlVigenciaHoras));

            _contexto.Comprobantes.Add(comprobante);
        }
        catch (InvalidOperationException ex)
        {
            // Falta la clave de cifrado: falla explícita, sin comprobante a medias
            // ni confirmación (CU-04, RB-3).
            throw new ComprobanteNoEmitidoException(
                "No se pudo emitir el comprobante: falta la clave de cifrado (Comprobantes:ClaveCifrado).",
                ex);
        }
    }
}
