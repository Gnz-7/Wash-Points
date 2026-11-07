using WashPoints.Application.Reembolsos;
using WashPoints.Domain.Puertos;
using WashPoints.Domain.Reembolsos;

namespace WashPoints.UnitTests.Reembolsos;

/// <summary>
/// T-156: clasificación, backoff, control de intentos y reconciliación del
/// servicio que ejecuta los reembolsos (SPECS/slice-15 D-15.4/D-15.5).
/// </summary>
public sealed class ProcesarReembolsosServicioTest
{
    private static readonly DateTime Ahora = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TurnoId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static OpcionesReembolso Opciones() => new()
    {
        LoteMaximo = 10,
        MaximoIntentos = 3,
        LeaseMinutos = 5,
        BackoffMinutos = [1, 5, 15],
    };

    private static Reembolso Pendiente(decimal monto = 3000m) =>
        Reembolso.Registrar(
            TurnoId,
            EstadoReembolso.Pendiente,
            MotivoReembolso.ClienteEnPlazo,
            monto,
            Ahora.AddHours(-1));

    private static ProcesarReembolsosServicio Servicio(
        RepositorioFalso repositorio,
        PasarelaFalsa pasarela,
        OpcionesReembolso? opciones = null) =>
        new(repositorio, pasarela, opciones ?? Opciones(), () => Ahora, (_, _) => 0);

    [Fact]
    public async Task UnReintegroAprobadoQuedaProcesadoConSuId()
    {
        var reembolso = Pendiente();
        var repositorio = new RepositorioFalso(reembolso, pagoId: "77");
        var pasarela = new PasarelaFalsa
        {
            RespuestaReembolso = ResultadoReembolso.Aprobado("900"),
        };

        var resultado = await Servicio(repositorio, pasarela).EjecutarTickAsync(CancellationToken.None);

        Assert.Equal(1, resultado.Procesados);
        Assert.Equal(0, resultado.Reintentados);
        Assert.Equal(0, resultado.Fallidos);
        Assert.Equal(EstadoReembolso.Procesado, reembolso.Estado);
        Assert.Equal("900", reembolso.MpRefundId);
        Assert.Equal(Ahora, reembolso.ProcesadoEn);
        Assert.Equal(1, reembolso.Intentos);
        Assert.Null(reembolso.ReclamadoEn);
        Assert.Equal(1, repositorio.Guardados);
        Assert.Equal($"reembolso-{reembolso.Id}", pasarela.ClaveIdempotencia);
        Assert.Equal(3000m, pasarela.MontoReembolsado);
    }

    [Fact]
    public async Task UnReintegroPendienteProgramaElPrimerBackoff()
    {
        var reembolso = Pendiente();
        var repositorio = new RepositorioFalso(reembolso, pagoId: "77");
        var pasarela = new PasarelaFalsa
        {
            RespuestaReembolso = ResultadoReembolso.Pendiente("900", "en proceso"),
        };

        var resultado = await Servicio(repositorio, pasarela).EjecutarTickAsync(CancellationToken.None);

        Assert.Equal(1, resultado.Reintentados);
        Assert.Equal(EstadoReembolso.Pendiente, reembolso.Estado);
        Assert.Equal(Ahora.AddMinutes(1), reembolso.ProximoIntentoEn);
        Assert.Null(reembolso.ReclamadoEn);
        Assert.Equal("900", reembolso.MpRefundId);
        Assert.Equal(1, reembolso.Intentos);
        Assert.Contains("en proceso", reembolso.UltimoError);
    }

    [Fact]
    public async Task UnRechazoDefinitivoMarcaFallidoSinReintento()
    {
        var reembolso = Pendiente();
        var repositorio = new RepositorioFalso(reembolso, pagoId: "77");
        var pasarela = new PasarelaFalsa
        {
            RespuestaReembolso = ResultadoReembolso.Rechazado("pago no reembolsable", reintentable: false),
        };

        var resultado = await Servicio(repositorio, pasarela).EjecutarTickAsync(CancellationToken.None);

        Assert.Equal(1, resultado.Fallidos);
        Assert.Equal(EstadoReembolso.Fallido, reembolso.Estado);
        Assert.Null(reembolso.ProximoIntentoEn);
        Assert.Contains("no reembolsable", reembolso.UltimoError);
    }

    [Fact]
    public async Task UnRechazoReintentableVuelveALaCola()
    {
        var reembolso = Pendiente();
        var repositorio = new RepositorioFalso(reembolso, pagoId: "77");
        var pasarela = new PasarelaFalsa
        {
            RespuestaReembolso = ResultadoReembolso.Rechazado("timeout", reintentable: true),
        };

        var resultado = await Servicio(repositorio, pasarela).EjecutarTickAsync(CancellationToken.None);

        Assert.Equal(1, resultado.Reintentados);
        Assert.Equal(EstadoReembolso.Pendiente, reembolso.Estado);
        Assert.Equal(Ahora.AddMinutes(1), reembolso.ProximoIntentoEn);
    }

    [Fact]
    public async Task AlAgotarLosIntentosElReembolsoQuedaFallido()
    {
        var reembolso = Pendiente();
        var repositorio = new RepositorioFalso(reembolso, pagoId: "77");
        var pasarela = new PasarelaFalsa
        {
            RespuestaReembolso = ResultadoReembolso.Pendiente(null, "sigue pendiente"),
        };
        var opciones = Opciones();
        opciones.MaximoIntentos = 1;

        var resultado = await Servicio(repositorio, pasarela, opciones)
            .EjecutarTickAsync(CancellationToken.None);

        Assert.Equal(1, resultado.Fallidos);
        Assert.Equal(EstadoReembolso.Fallido, reembolso.Estado);
        Assert.Contains("Se agotaron los 1 intentos", reembolso.UltimoError);
    }

    [Fact]
    public async Task UnTurnoSinPagoAprobadoNoLlamaALaPasarela()
    {
        var reembolso = Pendiente();
        var repositorio = new RepositorioFalso(reembolso, pagoId: null);
        var pasarela = new PasarelaFalsa();

        var resultado = await Servicio(repositorio, pasarela).EjecutarTickAsync(CancellationToken.None);

        Assert.Equal(1, resultado.Fallidos);
        Assert.Equal(EstadoReembolso.Fallido, reembolso.Estado);
        Assert.Equal(0, pasarela.ReembolsosSolicitados);
    }

    [Fact]
    public async Task UnErrorDeRedProgramaElReintento()
    {
        var reembolso = Pendiente();
        var repositorio = new RepositorioFalso(reembolso, pagoId: "77");
        var pasarela = new PasarelaFalsa
        {
            Excepcion = new PasarelaNoDisponibleException("sin red"),
        };

        var resultado = await Servicio(repositorio, pasarela).EjecutarTickAsync(CancellationToken.None);

        Assert.Equal(1, resultado.Reintentados);
        Assert.Equal(EstadoReembolso.Pendiente, reembolso.Estado);
        Assert.Contains("sin red", reembolso.UltimoError);
    }

    [Fact]
    public async Task UnReintegroYaSolicitadoSeReconciliaEnVezDePedirseDeNuevo()
    {
        var reembolso = Pendiente();
        reembolso.RegistrarReintegroEnCurso("900");
        var repositorio = new RepositorioFalso(reembolso, pagoId: "77");
        var pasarela = new PasarelaFalsa
        {
            RespuestaConsulta = ResultadoReembolso.Aprobado("900"),
        };

        var resultado = await Servicio(repositorio, pasarela).EjecutarTickAsync(CancellationToken.None);

        Assert.Equal(1, resultado.Procesados);
        Assert.Equal(0, pasarela.ReembolsosSolicitados);
        Assert.Equal(1, pasarela.ConsultasRealizadas);
        Assert.Equal(EstadoReembolso.Procesado, reembolso.Estado);
    }

    [Fact]
    public async Task UnLoteVacioNoHaceNada()
    {
        var repositorio = new RepositorioFalso(null, pagoId: null);
        var pasarela = new PasarelaFalsa();

        var resultado = await Servicio(repositorio, pasarela).EjecutarTickAsync(CancellationToken.None);

        Assert.Equal(new ResultadoProcesamientoReembolsos(0, 0, 0), resultado);
        Assert.Equal(1, repositorio.Guardados);
    }

    private sealed class RepositorioFalso(Reembolso? reembolso, string? pagoId) : IReembolsoRepositorio
    {
        public int Guardados { get; private set; }

        public Task AgregarAsync(Reembolso nuevo, CancellationToken ct) => Task.CompletedTask;

        public Task<Reembolso?> ObtenerPorTurnoAsync(Guid turnoId, CancellationToken ct) =>
            Task.FromResult(reembolso);

        public Task<IReadOnlyList<Reembolso>> ReclamarPendientesAsync(
            int lote, DateTime ahoraUtc, TimeSpan lease, CancellationToken ct)
        {
            if (reembolso is null)
            {
                return Task.FromResult<IReadOnlyList<Reembolso>>([]);
            }

            reembolso.Reclamar(ahoraUtc);
            return Task.FromResult<IReadOnlyList<Reembolso>>([reembolso]);
        }

        public Task<string?> ObtenerPagoAprobadoAsync(Guid turnoId, CancellationToken ct) =>
            Task.FromResult(pagoId);

        public Task GuardarCambiosAsync(CancellationToken ct)
        {
            Guardados++;
            return Task.CompletedTask;
        }
    }

    private sealed class PasarelaFalsa : IPasarelaPagos
    {
        public ResultadoReembolso RespuestaReembolso { get; init; } =
            ResultadoReembolso.Aprobado("refund-1");

        public ResultadoReembolso RespuestaConsulta { get; init; } =
            ResultadoReembolso.Aprobado("refund-1");

        public Exception? Excepcion { get; init; }

        public int ReembolsosSolicitados { get; private set; }
        public int ConsultasRealizadas { get; private set; }
        public string? ClaveIdempotencia { get; private set; }
        public decimal MontoReembolsado { get; private set; }

        public Task<PreferenciaCreada> CrearPreferenciaAsync(
            PreferenciaPago preferencia, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<ResultadoVerificacionPago> VerificarPagoAsync(string pagoIdExterno, CancellationToken ct) =>
            Task.FromResult(ResultadoVerificacionPago.Pendiente());

        public Task<ResultadoVerificacionPago> BuscarPagoPorTurnoAsync(Guid turnoId, CancellationToken ct) =>
            Task.FromResult(ResultadoVerificacionPago.Pendiente(turnoId));

        public Task<ResultadoReembolso> ReembolsarAsync(
            string pagoId, decimal monto, string? claveIdempotencia, CancellationToken ct)
        {
            if (Excepcion is not null)
            {
                throw Excepcion;
            }

            ReembolsosSolicitados++;
            MontoReembolsado = monto;
            ClaveIdempotencia = claveIdempotencia;
            return Task.FromResult(RespuestaReembolso);
        }

        public Task<ResultadoReembolso> ConsultarReembolsoAsync(
            string pagoId, string reembolsoId, CancellationToken ct)
        {
            if (Excepcion is not null)
            {
                throw Excepcion;
            }

            ConsultasRealizadas++;
            return Task.FromResult(RespuestaConsulta);
        }
    }
}
