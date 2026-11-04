using System.Data;
using Microsoft.Data.SqlClient;

namespace EvolutCRM.Services;

/// <summary>
/// Gasto da IA (OpenAI) e saldo estimado de crédito.
/// A OpenAI não informa o saldo por API, então:
///  - cada resposta da OpenAI passa pelo UsoOpenAiHandler, que lê os tokens usados e grava o custo estimado
///    (tabela de preços abaixo) em dbo.IaUsoChamada;
///  - o administrador informa o saldo que aparece no painel da OpenAI (dbo.IaCreditoSaldo);
///  - saldo estimado = último saldo informado − gasto registrado desde então.
/// Atenção (amarelo) em US$ 2,00 e crítico (vermelho) em US$ 1,00.
/// Transcrição de áudio do WhatsApp (feita fora do HELP) não entra na conta; custa pouco.
/// </summary>
public sealed class IaUsoService
{
    public const decimal LimiteAtencao = 2.00m;
    public const decimal LimiteCritico = 1.00m;

    private readonly string _cs;
    private static volatile bool _tabelasOk;
    private DateTime _ultimaLimpeza = DateTime.MinValue;

    public IaUsoService(IConfiguration config)
    {
        _cs = config.GetConnectionString("Connection")!;
    }

    // ───────────── Preços (US$ por 1 milhão de tokens: entrada, entrada em cache, saída) ─────────────
    // Estimativa: se a OpenAI mudar os preços, ajuste aqui. Modelo desconhecido usa o preço do gpt-4.1 (pelo alto).
    private static readonly (string Prefixo, decimal Entrada, decimal Cache, decimal Saida)[] Precos =
    {
        ("gpt-4.1-nano", 0.10m, 0.025m, 0.40m),
        ("gpt-4.1-mini", 0.40m, 0.10m, 1.60m),
        ("gpt-4.1", 2.00m, 0.50m, 8.00m),
        ("gpt-4o-mini", 0.15m, 0.075m, 0.60m),
        ("gpt-4o", 2.50m, 1.25m, 10.00m),
        ("gpt-5-nano", 0.05m, 0.005m, 0.40m),
        ("gpt-5-mini", 0.25m, 0.025m, 2.00m),
        ("gpt-5", 1.25m, 0.125m, 10.00m),
        ("o4-mini", 1.10m, 0.275m, 4.40m),
        ("text-embedding-3-small", 0.02m, 0.02m, 0m),
        ("text-embedding-3-large", 0.13m, 0.13m, 0m),
    };
    private const decimal PesquisaWebPorChamada = 0.025m;   // ferramenta de busca na web (por busca)
    private const decimal AudioPorSegundo = 0.0001m;         // transcrição (US$ 0,006 por minuto)

    public static decimal CalcularCusto(string modelo, long entrada, long cache, long saida, int buscasWeb = 0, decimal segundosAudio = 0)
    {
        var m = (modelo ?? "").ToLowerInvariant();
        var p = Precos.Where(x => m.StartsWith(x.Prefixo)).OrderByDescending(x => x.Prefixo.Length).FirstOrDefault();
        if (p.Prefixo == null) p = ("?", 2.00m, 0.50m, 8.00m);

        var semCache = Math.Max(0, entrada - cache);
        var custo = (semCache * p.Entrada + cache * p.Cache + saida * p.Saida) / 1_000_000m;
        custo += buscasWeb * PesquisaWebPorChamada;
        custo += segundosAudio * AudioPorSegundo;
        return Math.Round(custo, 6);
    }

    // ───────────── Tabelas ─────────────
    private async Task GarantirTabelasAsync(SqlConnection conn)
    {
        if (_tabelasOk) return;
        await using var cmd = new SqlCommand(@"
IF OBJECT_ID('dbo.IaUsoChamada', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.IaUsoChamada
    (
        Codigo        BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_IaUsoChamada PRIMARY KEY,
        DataHora      DATETIME      NOT NULL CONSTRAINT DF_IaUsoChamada_DataHora DEFAULT (GETDATE()),
        Modelo        VARCHAR(80)   NOT NULL,
        Endpoint      VARCHAR(60)   NOT NULL,
        TokensEntrada INT           NOT NULL,
        TokensCache   INT           NOT NULL,
        TokensSaida   INT           NOT NULL,
        Extra         VARCHAR(40)   NULL,
        CustoUsd      DECIMAL(12,6) NOT NULL
    );
    CREATE INDEX IX_IaUsoChamada_DataHora ON dbo.IaUsoChamada (DataHora) INCLUDE (CustoUsd, Modelo);
END

IF OBJECT_ID('dbo.IaCreditoSaldo', 'U') IS NULL
    CREATE TABLE dbo.IaCreditoSaldo
    (
        Codigo       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_IaCreditoSaldo PRIMARY KEY,
        SaldoUsd     DECIMAL(12,2) NOT NULL,
        InformadoEm  DATETIME      NOT NULL CONSTRAINT DF_IaCreditoSaldo_Em DEFAULT (GETDATE()),
        InformadoPor VARCHAR(100)  NOT NULL
    );", conn);
        await cmd.ExecuteNonQueryAsync();
        _tabelasOk = true;
    }

    // ───────────── Registro de cada chamada (chamado pelo UsoOpenAiHandler) ─────────────
    public async Task RegistrarAsync(string modelo, string endpoint, long entrada, long cache, long saida,
        int buscasWeb = 0, decimal segundosAudio = 0)
    {
        try
        {
            var custo = CalcularCusto(modelo, entrada, cache, saida, buscasWeb, segundosAudio);
            string? extra = buscasWeb > 0 ? $"busca web x{buscasWeb}" : segundosAudio > 0 ? $"audio {segundosAudio:0}s" : null;

            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await GarantirTabelasAsync(conn);
            await using var cmd = new SqlCommand(@"
INSERT INTO dbo.IaUsoChamada (Modelo, Endpoint, TokensEntrada, TokensCache, TokensSaida, Extra, CustoUsd)
VALUES (@m, @e, @i, @c, @o, @x, @custo);", conn);
            cmd.Parameters.Add("@m", SqlDbType.VarChar, 80).Value = Cortar(modelo, 80);
            cmd.Parameters.Add("@e", SqlDbType.VarChar, 60).Value = Cortar(endpoint, 60);
            cmd.Parameters.AddWithValue("@i", (int)Math.Min(int.MaxValue, entrada));
            cmd.Parameters.AddWithValue("@c", (int)Math.Min(int.MaxValue, cache));
            cmd.Parameters.AddWithValue("@o", (int)Math.Min(int.MaxValue, saida));
            cmd.Parameters.Add("@x", SqlDbType.VarChar, 40).Value = (object?)extra ?? DBNull.Value;
            var pCusto = cmd.Parameters.Add("@custo", SqlDbType.Decimal);
            pCusto.Precision = 12; pCusto.Scale = 6; pCusto.Value = custo;
            await cmd.ExecuteNonQueryAsync();

            // limpeza: guarda 120 dias
            if (DateTime.Now - _ultimaLimpeza > TimeSpan.FromHours(12))
            {
                _ultimaLimpeza = DateTime.Now;
                await using var del = new SqlCommand("DELETE FROM dbo.IaUsoChamada WHERE DataHora < DATEADD(DAY, -120, GETDATE());", conn);
                await del.ExecuteNonQueryAsync();
            }
        }
        catch (Exception ex)
        {
            LogHelper.Log("[IA USO] Não registrou o gasto: " + ex.Message);
        }
    }

    // ───────────── Saldo ─────────────
    public async Task InformarSaldoAsync(decimal saldoUsd, string usuario)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await GarantirTabelasAsync(conn);
        await using var cmd = new SqlCommand(
            "INSERT INTO dbo.IaCreditoSaldo (SaldoUsd, InformadoPor) VALUES (@s, @u);", conn);
        var p = cmd.Parameters.Add("@s", SqlDbType.Decimal);
        p.Precision = 12; p.Scale = 2; p.Value = Math.Round(saldoUsd, 2);
        cmd.Parameters.Add("@u", SqlDbType.VarChar, 100).Value = Cortar((usuario ?? "").Trim().ToUpperInvariant(), 100);
        await cmd.ExecuteNonQueryAsync();
    }

    public sealed record GastoModelo(string Modelo, int Chamadas, decimal CustoUsd);

    public sealed class Resumo
    {
        public decimal? SaldoInformado { get; init; }
        public DateTime? InformadoEm { get; init; }
        public string InformadoPor { get; init; } = "";
        public decimal GastoDesdeSaldo { get; init; }
        public decimal GastoHoje { get; init; }
        public decimal GastoMes { get; init; }
        public int ChamadasHoje { get; init; }
        public List<GastoModelo> PorModeloHoje { get; init; } = new();

        public decimal? SaldoEstimado => SaldoInformado.HasValue ? SaldoInformado.Value - GastoDesdeSaldo : null;

        /// <summary>sem-saldo / ok / atencao / critico</summary>
        public string Estado => SaldoEstimado is not decimal s ? "sem-saldo"
            : s <= LimiteCritico ? "critico"
            : s <= LimiteAtencao ? "atencao"
            : "ok";
    }

    public async Task<Resumo> ObterResumoAsync()
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await GarantirTabelasAsync(conn);

        await using var cmd = new SqlCommand(@"
DECLARE @Saldo DECIMAL(12,2), @Em DATETIME, @Por VARCHAR(100);
SELECT TOP 1 @Saldo = SaldoUsd, @Em = InformadoEm, @Por = InformadoPor FROM dbo.IaCreditoSaldo ORDER BY Codigo DESC;

DECLARE @Hoje DATETIME = CAST(CAST(GETDATE() AS DATE) AS DATETIME);
DECLARE @Mes DATETIME = DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1);

SELECT @Saldo, @Em, ISNULL(@Por, ''),
       (SELECT ISNULL(SUM(CustoUsd), 0) FROM dbo.IaUsoChamada WHERE @Em IS NOT NULL AND DataHora >= @Em),
       (SELECT ISNULL(SUM(CustoUsd), 0) FROM dbo.IaUsoChamada WHERE DataHora >= @Hoje),
       (SELECT ISNULL(SUM(CustoUsd), 0) FROM dbo.IaUsoChamada WHERE DataHora >= @Mes),
       (SELECT COUNT(*) FROM dbo.IaUsoChamada WHERE DataHora >= @Hoje);

SELECT TOP 6 Modelo, COUNT(*), SUM(CustoUsd)
FROM dbo.IaUsoChamada WHERE DataHora >= @Hoje
GROUP BY Modelo ORDER BY SUM(CustoUsd) DESC;", conn);

        await using var rd = await cmd.ExecuteReaderAsync();
        await rd.ReadAsync();
        decimal? saldo = rd.IsDBNull(0) ? null : rd.GetDecimal(0);
        DateTime? em = rd.IsDBNull(1) ? null : rd.GetDateTime(1);
        var por = rd.GetString(2);
        var desde = rd.GetDecimal(3);
        var hoje = rd.GetDecimal(4);
        var mes = rd.GetDecimal(5);
        var qtdHoje = rd.GetInt32(6);

        var modelos = new List<GastoModelo>();
        if (await rd.NextResultAsync())
            while (await rd.ReadAsync())
                modelos.Add(new GastoModelo(rd.GetString(0), rd.GetInt32(1), rd.GetDecimal(2)));

        return new Resumo
        {
            SaldoInformado = saldo, InformadoEm = em, InformadoPor = por,
            GastoDesdeSaldo = desde, GastoHoje = hoje, GastoMes = mes, ChamadasHoje = qtdHoje,
            PorModeloHoje = modelos
        };
    }

    private static string Cortar(string? s, int max) => (s ?? "").Length > max ? s![..max] : (s ?? "");
}
