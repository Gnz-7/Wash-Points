using WashPoints.Domain.Reembolsos;

namespace WashPoints.Api;

/// <summary>
/// Traduce los enums de reembolso al <c>snake_case</c> que expone la API
/// (SPECS/slice-14 §6). El contrato viaja en snake_case igual que el estado del
/// turno, así el cliente no tiene que conocer los nombres internos de los enums.
/// </summary>
internal static class ReembolsosAJson
{
    /// <summary>Estado del reembolso tal como lo lee el cliente.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Estado desconocido.</exception>
    public static string Estado(EstadoReembolso estado) => estado switch
    {
        EstadoReembolso.Pendiente => "pendiente",
        EstadoReembolso.Procesado => "procesado",
        EstadoReembolso.Fallido => "fallido",
        EstadoReembolso.NoAplica => "no_aplica",
        _ => throw new ArgumentOutOfRangeException(nameof(estado), estado, null),
    };

    /// <summary>Motivo del reembolso tal como lo lee el cliente.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Motivo desconocido.</exception>
    public static string Motivo(MotivoReembolso motivo) => motivo switch
    {
        MotivoReembolso.ClienteEnPlazo => "cliente_en_plazo",
        MotivoReembolso.ClienteFueraDePlazo => "cliente_fuera_de_plazo",
        MotivoReembolso.SinPago => "sin_pago",
        _ => throw new ArgumentOutOfRangeException(nameof(motivo), motivo, null),
    };
}
