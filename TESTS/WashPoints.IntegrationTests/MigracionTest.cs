using Microsoft.EntityFrameworkCore;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// Aplica la migración inicial contra Postgres real y verifica lo que EF no
/// puede validar por sí solo: la restricción EXCLUDE y la extensión de la que
/// depende.
///
/// Ver SPECS/slice-00-nucleo-turnos.md T-04.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class MigracionTest : IClassFixture<ContenedorPostgres>, IAsyncLifetime
{
    private readonly ContenedorPostgres _baseDeDatos;

    public MigracionTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    private WashPointsDbContext CrearContexto()
    {
        var opciones = new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options;
        return new WashPointsDbContext(opciones);
    }

    public async Task InitializeAsync()
    {
        await using var contexto = CrearContexto();
        await contexto.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task LaMigracionAplicaUnaSolaVez()
    {
        await using var contexto = CrearContexto();
        var aplicadas = await contexto.Database.GetAppliedMigrationsAsync();

        Assert.Contains(aplicadas, m => m.EndsWith("_Inicial", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LaRestriccionDeNoSolapamientoExiste()
    {
        var existe = await _baseDeDatos.ConsultarEscalarAsync<bool>("""
            SELECT EXISTS (
                SELECT 1
                FROM pg_constraint
                WHERE conname = 'turnos_no_solapamiento'
                  AND contype = 'x'
            )
            """);

        Assert.True(existe, "La restricción de exclusión no se creó en la migración.");
    }

    [Fact]
    public async Task BtreeGistQuedaInstaladaPorLaMigracion()
    {
        // La migración ejecuta CREATE EXTENSION btree_gist; sin ella, el
        // EXCLUDE con igualdad sobre uuid no puede crearse.
        var instalada = await _baseDeDatos.ConsultarEscalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'btree_gist')");

        Assert.True(instalada);
    }

    [Fact]
    public async Task ElPredicadoDelExcludeIncluyeElPendienteDePago()
    {
        // Slice-01 (T-11): el predicado pasa a 3 estados. El pendiente_pago
        // vigente bloquea el intervalo (regla de producto, SPECS/slice-01 §3).
        var definicion = await _baseDeDatos.ConsultarEscalarAsync<string>(
            "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'turnos_no_solapamiento'");

        Assert.NotNull(definicion);
        Assert.Contains("'pendiente_pago'", definicion);
        Assert.Contains("'confirmado'", definicion);
        Assert.Contains("'en_curso'", definicion);
    }

    [Fact]
    public async Task ElExcludeNoIncluyeExpiraEnEnElPredicado()
    {
        // Los índices parciales no se re-evalúan con el paso del tiempo: la
        // condición temporal (expira_en > now()) vive en la consulta y en el
        // worker, nunca en el predicado del EXCLUDE (SPECS/slice-01 T-11).
        var definicion = await _baseDeDatos.ConsultarEscalarAsync<string>(
            "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'turnos_no_solapamiento'");

        Assert.DoesNotContain("expira_en", definicion);
    }

    [Fact]
    public async Task TurnosTieneLasColumnasDelSlice01()
    {
        var expiraEn = await _baseDeDatos.ConsultarEscalarAsync<bool>("""
            SELECT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_name = 'turnos' AND column_name = 'expira_en'
            )
            """);
        var idempotencyKey = await _baseDeDatos.ConsultarEscalarAsync<bool>("""
            SELECT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_name = 'turnos' AND column_name = 'idempotency_key'
            )
            """);

        Assert.True(expiraEn, "Falta la columna turnos.expira_en (TTL de la seña).");
        Assert.True(idempotencyKey, "Falta la columna turnos.idempotency_key (idempotencia de reserva).");
    }

    [Fact]
    public async Task IdempotencyKeyTieneIndiceUnicoParcial()
    {
        // La unicidad debe ser parcial (WHERE idempotency_key IS NOT NULL): la
        // clave solo existe mientras hay un intento de reserva (SPECS/slice-01
        // §3, T-11). Un UNIQUE trivial NULL-able no fuerza unicidad real.
        var indice = await _baseDeDatos.ConsultarEscalarAsync<bool>("""
            SELECT EXISTS (
                SELECT 1
                FROM pg_index i
                JOIN pg_class t ON t.oid = i.indrelid
                JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = i.indkey[0]
                WHERE t.relname = 'turnos'
                  AND i.indisunique
                  AND i.indpred IS NOT NULL
                  AND a.attname = 'idempotency_key'
            )
            """);

        Assert.True(indice, "Falta el índice único parcial sobre turnos.idempotency_key.");
    }

    [Fact]
    public async Task LaTablaPagosExisteConUnicidadesDeIdempotencia()
    {
        var existe = await _baseDeDatos.ConsultarEscalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'pagos')");
        Assert.True(existe, "Falta la tabla pagos (registro evaluable del evento de pago).");

        // mp_payment_id único: un webhook duplicado no duplica la fila.
        var unicoPorPago = await _baseDeDatos.ConsultarEscalarAsync<bool>("""
            SELECT EXISTS (
                SELECT 1
                FROM pg_index i
                JOIN pg_class t ON t.oid = i.indrelid
                JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = i.indkey[0]
                WHERE t.relname = 'pagos'
                  AND i.indisunique
                  AND a.attname = 'mp_payment_id'
            )
            """);

        // UNIQUE(turno_id): un turno no puede tener dos pagos asociados (RB-1).
        var unicoPorTurno = await _baseDeDatos.ConsultarEscalarAsync<bool>("""
            SELECT EXISTS (
                SELECT 1
                FROM pg_index i
                JOIN pg_class t ON t.oid = i.indrelid
                JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = i.indkey[0]
                WHERE t.relname = 'pagos'
                  AND i.indisunique
                  AND a.attname = 'turno_id'
            )
            """);

        Assert.True(unicoPorPago, "pagos.mp_payment_id no es único (idempotencia del webhook).");
        Assert.True(unicoPorTurno, "pagos.turno_id no es único (un pago por turno).");
    }

    [Fact]
    public async Task GuardarYLeerTurnoResuelveElRangoEnUtc()
    {
        // Recorrido de ida y vuelta del convertidor: el dominio guarda UTC, la
        // base guarda hora local, y al leer ambos turnos vuelven a ser el mismo.
        // Es la prueba cruzada de T-09 en su forma mínima.
        await using var contexto = CrearContexto();

        var lavadero = Lavadero.Crear("Lavadero Central");
        var puesto = Puesto.Crear(lavadero.Id, "Puesto 1");
        var tipo = TipoLavado.Crear(lavadero.Id, "Exterior", 30, 15000m, 3000m);
        var cliente = Cliente.Crear("Cliente de Prueba");

        contexto.AddRange(lavadero, puesto, tipo, cliente);
        await contexto.SaveChangesAsync();

        var inicioUtc = new DateTime(2026, 10, 10, 13, 0, 0, DateTimeKind.Utc);
        var finUtc = new DateTime(2026, 10, 10, 14, 0, 0, DateTimeKind.Utc);
        var turno = Turno.Crear(
            lavaderoId: lavadero.Id,
            puestoId: puesto.Id,
            clienteId: cliente.Id,
            tipoLavadoId: tipo.Id,
            inicioUtc: inicioUtc,
            finUtc: finUtc,
            precioSena: tipo.PrecioSena);

        contexto.Turnos.Add(turno);
        await contexto.SaveChangesAsync();

        // El guardado aplica el convertidor; el siguiente contexto re-lee lo
        // persistido, de modo que no quedan trucos de cache de rastreo.
        await using var contexto2 = CrearContexto();
        var turnoLeido = await contexto2.Turnos
            .Where(t => t.Id == turno.Id)
            .SingleAsync();

        Assert.Equal(inicioUtc, turnoLeido.InicioUtc);
        Assert.Equal(finUtc, turnoLeido.FinUtc);
    }

    [Fact]
    public async Task LaTablaComprobantesExisteConUnicidades()
    {
        // Slice-02 (T-24, RB-3): el comprobante es el token de presencia.
        var existe = await _baseDeDatos.ConsultarEscalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'comprobantes')");
        Assert.True(existe, "Falta la tabla comprobantes (token de presencia de RB-3).");

        // UNIQUE(turno_id): un turno confirmado tiene exactamente un comprobante.
        var unicoPorTurno = await _baseDeDatos.ConsultarEscalarAsync<bool>("""
            SELECT EXISTS (
                SELECT 1
                FROM pg_index i
                JOIN pg_class t ON t.oid = i.indrelid
                JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = i.indkey[0]
                WHERE t.relname = 'comprobantes'
                  AND i.indisunique
                  AND a.attname = 'turno_id'
            )
            """);

        // hash único: el canje localiza por hash y dos comprobantes no lo comparten.
        var unicoPorHash = await _baseDeDatos.ConsultarEscalarAsync<bool>("""
            SELECT EXISTS (
                SELECT 1
                FROM pg_index i
                JOIN pg_class t ON t.oid = i.indrelid
                JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = i.indkey[0]
                WHERE t.relname = 'comprobantes'
                  AND i.indisunique
                  AND a.attname = 'hash'
            )
            """);

        Assert.True(unicoPorTurno, "comprobantes.turno_id no es único (un comprobante por turno).");
        Assert.True(unicoPorHash, "comprobantes.hash no es único (localización/canje por hash).");
    }

    [Fact]
    public async Task ComprobantesTieneLasColumnasDelSlice02()
    {
        async Task<bool> ExisteColumna(string columna) =>
            await _baseDeDatos.ConsultarEscalarAsync<bool>($"""
                SELECT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_name = 'comprobantes' AND column_name = '{columna}'
                )
                """);

        Assert.True(await ExisteColumna("hash"), "Falta comprobantes.hash.");
        Assert.True(await ExisteColumna("token_cifrado"), "Falta comprobantes.token_cifrado (recuperación sin claro en reposo).");
        Assert.True(await ExisteColumna("expira_en"), "Falta comprobantes.expira_en (vencimiento).");
        Assert.True(await ExisteColumna("usado_at"), "Falta comprobantes.usado_at (canje único).");
        Assert.True(await ExisteColumna("validado_offline"), "Falta comprobantes.validado_offline.");
        Assert.True(await ExisteColumna("creado_en"), "Falta comprobantes.creado_en.");
    }

    [Fact]
    public async Task LaTablaOutboxExisteConIndiceParcial()
    {
        // Slice-03 (T-34, RB-2): la outbox es el mecanismo de sincronización;
        // toda escritura de ocupación encola aquí su evento en el mismo commit.
        var existe = await _baseDeDatos.ConsultarEscalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'outbox')");
        Assert.True(existe, "Falta la tabla outbox (eventos de ocupación para SignalR).");

        async Task<bool> ExisteColumna(string columna) =>
            await _baseDeDatos.ConsultarEscalarAsync<bool>($"""
                SELECT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_name = 'outbox' AND column_name = '{columna}'
                )
                """);

        Assert.True(await ExisteColumna("tipo"), "Falta outbox.tipo.");
        Assert.True(await ExisteColumna("payload"), "Falta outbox.payload.");
        Assert.True(await ExisteColumna("publicado_en"), "Falta outbox.publicado_en.");
        Assert.True(await ExisteColumna("creado_en"), "Falta outbox.creado_en.");

        // La publicación solo mira las filas no publicadas: el índice debe ser
        // PARCIAL (WHERE publicado_en IS NULL) para no escanear el histórico.
        var indiceParcial = await _baseDeDatos.ConsultarEscalarAsync<bool>("""
            SELECT EXISTS (
                SELECT 1
                FROM pg_index i
                JOIN pg_class t ON t.oid = i.indrelid
                JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = i.indkey[0]
                WHERE t.relname = 'outbox'
                  AND i.indpred IS NOT NULL
                  AND a.attname = 'publicado_en'
            )
            """);

        Assert.True(indiceParcial, "Falta el índice parcial sobre outbox.publicado_en (publicado_en IS NULL).");
    }

    [Fact]
    public async Task LaTablaUsuariosExisteConRestricciones()
    {
        // Slice-05 (T-51, D-05.1): la identidad server-side se persiste en
        // `usuarios`. La tabla no existía antes de esta migración.
        var existe = await _baseDeDatos.ConsultarEscalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'usuarios')");
        Assert.True(existe, "Falta la tabla usuarios (identidad server-side, CU-10).");

        async Task<bool> ExisteColumna(string columna) =>
            await _baseDeDatos.ConsultarEscalarAsync<bool>($"""
                SELECT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_name = 'usuarios' AND column_name = '{columna}'
                )
                """);

        Assert.True(await ExisteColumna("id"), "Falta usuarios.id.");
        Assert.True(await ExisteColumna("nombre_usuario"), "Falta usuarios.nombre_usuario.");
        Assert.True(await ExisteColumna("hash_contrasena"), "Falta usuarios.hash_contrasena.");
        Assert.True(await ExisteColumna("rol"), "Falta usuarios.rol.");
        Assert.True(await ExisteColumna("lavadero_id"), "Falta usuarios.lavadero_id.");
        Assert.True(await ExisteColumna("activo"), "Falta usuarios.activo.");
        Assert.True(await ExisteColumna("creado_en"), "Falta usuarios.creado_en.");

        // UNIQUE(nombre_usuario): la credencial se localiza por nombre y no se repite.
        var unicoPorNombre = await _baseDeDatos.ConsultarEscalarAsync<bool>("""
            SELECT EXISTS (
                SELECT 1
                FROM pg_index i
                JOIN pg_class t ON t.oid = i.indrelid
                JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = i.indkey[0]
                WHERE t.relname = 'usuarios'
                  AND i.indisunique
                  AND a.attname = 'nombre_usuario'
            )
            """);
        Assert.True(unicoPorNombre, "usuarios.nombre_usuario no es único.");

        // D-05.1 (rol): solo se aceptan los roles 'cliente' y 'administrador'.
        var checkRolValido = await _baseDeDatos.ConsultarEscalarAsync<bool>("""
            SELECT EXISTS (
                SELECT 1 FROM pg_constraint
                WHERE conrelid = 'usuarios'::regclass
                  AND contype = 'c'
                  AND pg_get_constraintdef(oid) LIKE '%cliente%'
            )
            """);
        Assert.True(checkRolValido, "Falta el CHECK que restringe usuarios.rol a los roles válidos.");

        // D-05.1 (lavadero_id): un 'administrador' siempre pertenece a un lavadero.
        var checkAdminRequiereLavadero = await _baseDeDatos.ConsultarEscalarAsync<bool>("""
            SELECT EXISTS (
                SELECT 1 FROM pg_constraint
                WHERE conrelid = 'usuarios'::regclass
                  AND contype = 'c'
                  AND pg_get_constraintdef(oid) LIKE '%administrador%'
                  AND pg_get_constraintdef(oid) LIKE '%lavadero_id%'
            )
            """);
        Assert.True(checkAdminRequiereLavadero, "Falta el CHECK administrador ⇒ lavadero_id NOT NULL.");
    }
}
