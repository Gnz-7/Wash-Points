using WashPoints.Application.Reembolsos;
using WashPoints.Domain.MercadoPago;
using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Puertos;
using WashPoints.Domain.Reembolsos;
using WashPoints.Domain.Turnos;

namespace WashPoints.UnitTests.Reembolsos;

/// <summary>
/// T-156: clasificación, backoff, control de intentos y reconciliación del
/// servicio que ejecuta los reembolsos (SPECS/slice-15 D-15.4/D-15.5).
/// Slice-20 (D-20.5): cada reintegro se pide con el token del lavadero del
/// pago; un lavadero desvinculado deja el reembolso fallido con motivo.
/// </summary>
public sealed class ProcesarReembolsosServicioTest
{
    private static readonly DateTime Ahora = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TurnoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid LavaderoId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

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

    private static Turno TurnoDelLavadero() =>
        Turno.Crear(
            lavaderoId: LavaderoId,
            puestoId: Guid.NewGuid(),
            clienteId: Guid.NewGuid(),
            tipoLavadoId: Guid.NewGuid(),
            inicioUtc: Ahora.AddHours(1),
            finUtc: Ahora.AddHours(1).AddMinutes(30),
            precioSena: 1000m,
            precioTotal: 15000m);

    private static ProcesarReembolsosServicio Servicio(
        RepositorioFalso repositorio,
        PasarelaFalsa pasarela,
        OpcionesReembolso? opciones = null,
        TokensFalso? tokensMp = null) =>
        new(
            repositorio,
            pasarela,
            tokensMp ?? new TokensFalso(),
            new TurnosFalso(TurnoDelLavadero()),
            opciones ?? Opciones(),
            () => Ahora,
            (_, _) => 0);

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

    [Fact]
    public async Task ElReintegroSePideConElTokenDelLavadero()
    {
        var reembolso = Pendiente();
        var repositorio = new RepositorioFalso(reembolso, pagoId: "77");
        var pasarela = new PasarelaFalsa
        {
            RespuestaReembolso = ResultadoReembolso.Aprobado("900"),
        };

        await Servicio(repositorio, pasarela).EjecutarTickAsync(CancellationToken.None);

        Assert.Equal("token-de-lavadero", pasarela.UltimoToken);
    }

    [Fact]
    public async Task UnLavaderoDesvinculadoDejaElReembolsoFallidoConMotivo()
    {
        var reembolso = Pendiente();
        var repositorio = new RepositorioFalso(reembolso, pagoId: "77");
        var pasarela = new PasarelaFalsa();
        var tokensMp = new TokensFalso { LanzarNoVinculado = true };

        var resultado = await Servicio(repositorio, pasarela, tokensMp: tokensMp)
            .EjecutarTickAsync(CancellationToken.None);

        Assert.Equal(1, resultado.Fallidos);
        Assert.Equal(EstadoReembolso.Fallido, reembolso.Estado);
        Assert.Contains("desvinculó", reembolso.UltimoError);
        Assert.Equal(0, pasarela.ReembolsosSolicitados);
    }

    [Fact]
    public async Task UnTokenRechazadoRefrescaYReintentaUnaVez()
    {
        var reembolso = Pendiente();
        var repositorio = new RepositorioFalso(reembolso, pagoId: "77");
        var pasarela = new PasarelaFalsa
        {
            Excepcion = new PasarelaTokenInvalidoException("el token fue rechazado (401)"),
        };
        var tokensMp = new TokensFalso();

        var resultado = await Servicio(repositorio, pasarela, tokensMp: tokensMp)
            .EjecutarTickAsync(CancellationToken.None);

        Assert.Equal(1, resultado.Procesados);
        Assert.Equal(EstadoReembolso.Procesado, reembolso.Estado);
        Assert.Equal(1, tokensMp.RefrescosPedidos);
        Assert.Equal("token-fresco", pasarela.UltimoToken);
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

        /// <summary>La excepción scripteada se lanza una sola vez (la primera llamada); el retry siguiente ya funciona.</summary>
        private int _llamadasConExcepcion;

        private bool LanzarExcepcion() =>
            Excepcion is not null && Interlocked.Increment(ref _llamadasConExcepcion) <= 1;

        public int ReembolsosSolicitados { get; private set; }
        public int ConsultasRealizadas { get; private set; }
        public string? ClaveIdempotencia { get; private set; }
        public decimal MontoReembolsado { get; private set; }
        public string? UltimoToken { get; private set; }

        public Task<PreferenciaCreada> CrearPreferenciaAsync(
            PreferenciaPago preferencia, string accessToken, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<ResultadoVerificacionPago> VerificarPagoAsync(
            string pagoIdExterno, string accessToken, CancellationToken ct) =>
            Task.FromResult(ResultadoVerificacionPago.Pendiente());

        public Task<ResultadoVerificacionPago> BuscarPagoPorTurnoAsync(
            Guid turnoId, string accessToken, CancellationToken ct) =>
            Task.FromResult(ResultadoVerificacionPago.Pendiente(turnoId));

        public Task<ResultadoReembolso> ReembolsarAsync(
            string pagoId, decimal monto, string? claveIdempotencia, string accessToken, CancellationToken ct)
        {
            UltimoToken = accessToken;

            if (LanzarExcepcion())
            {
                throw Excepcion!;
            }

            ReembolsosSolicitados++;
            MontoReembolsado = monto;
            ClaveIdempotencia = claveIdempotencia;
            return Task.FromResult(RespuestaReembolso);
        }

        public Task<ResultadoReembolso> ConsultarReembolsoAsync(
            string pagoId, string reembolsoId, string accessToken, CancellationToken ct)
        {
            UltimoToken = accessToken;

            if (LanzarExcepcion())
            {
                throw Excepcion!;
            }

            ConsultasRealizadas++;
            return Task.FromResult(RespuestaConsulta);
        }
    }

    /// <summary>Doble de <see cref="IOcupacionRepositorio"/> que devuelve el turno sembrado.</summary>
    private sealed class TurnosFalso(Turno turno) : IOcupacionRepositorio
    {
        public Task AgregarAsync(Turno nuevo, CancellationToken ct) => Task.CompletedTask;

        public Task<Turno?> ObtenerAsync(Guid turnoId, CancellationToken ct) =>
            Task.FromResult<Turno?>(turno);

        public Task<Turno?> ObtenerPorIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct) =>
            Task.FromResult<Turno?>(null);

        public Task<IReadOnlyList<Turno>> ListarDelDiaAsync(
            Guid lavaderoId, DateTime desdeUtc, DateTime hastaUtc, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Turno>>([]);

        public Task<IReadOnlyList<Turno>> ListarPorClienteAsync(Guid clienteId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Turno>>([]);

        public Task<bool> TieneTurnosVigentesAsync(
            Guid lavaderoId, Guid puestoId, DateTime ahoraUtc, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<bool> TieneTurnosVigentesDelTipoAsync(
            Guid lavaderoId, Guid tipoLavadoId, DateTime ahoraUtc, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<Turno>> ListarConfirmadosVencidosAsync(
            DateTime limiteUtc, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Turno>>([]);

        public Task GuardarAsync(CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>
    /// Doble laxo de <see cref="IObtenedorTokensMp"/>: entrega el mismo token
    /// siempre, salvo que se le pida lanzar <see cref="MercadoPagoNoVinculadoException"/>.
    /// </summary>
    private sealed class TokensFalso : IObtenedorTokensMp
    {
        public bool LanzarNoVinculado { get; init; }
        public int RefrescosPedidos { get; private set; }

        public Task<string> ObtenerTokenVigenteAsync(Guid lavaderoId, CancellationToken ct)
        {
            if (LanzarNoVinculado)
            {
                throw new MercadoPagoNoVinculadoException("sin vínculo");
            }

            return Task.FromResult("token-de-lavadero");
        }

        public Task<string> RefrescarTokenAsync(Guid lavaderoId, CancellationToken ct)
        {
            RefrescosPedidos++;

            if (LanzarNoVinculado)
            {
                throw new MercadoPagoNoVinculadoException("sin vínculo");
            }

            return Task.FromResult("token-fresco");
        }
    }
}
