using Microsoft.Data.SqlClient;

namespace EvolutCRM.Services;

/// <summary>
/// Colunas da curadoria da IA em AiTicketInsights (revisão humana e feedback dos atendentes).
/// São criadas sozinhas na primeira vez que a IA abre o banco (mesmo conteúdo de
/// Scripts/2026-10-28_IA_Curadoria.sql). Se o usuário do banco não puder alterar a tabela,
/// a IA continua funcionando só com os casos confirmados pelo cliente.
///
/// StatusRevisao:
///   NULL / 'pendente' = ainda não revisado (só entra na IA se SolucaoConfirmada = 1)
///   'aprovado'        = revisado e liberado para a IA
///   'descartado'      = revisado e jogado fora (nunca entra)
///   'suspenso'        = recebeu 2 ou mais 👎 nas sugestões: fica fora até alguém revisar
/// </summary>
public static class IaCuradoriaSchema
{
    private static volatile bool _verificado;
    private static volatile bool _colunasOk;
    private static readonly SemaphoreSlim _trava = new(1, 1);

    public const string Sql = @"
IF OBJECT_ID('dbo.AiTicketInsights', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.AiTicketInsights', 'StatusRevisao') IS NULL
        ALTER TABLE dbo.AiTicketInsights ADD StatusRevisao VARCHAR(20) NULL;
    IF COL_LENGTH('dbo.AiTicketInsights', 'QtdNegativos') IS NULL
        ALTER TABLE dbo.AiTicketInsights ADD QtdNegativos INT NOT NULL CONSTRAINT DF_AiTicketInsights_QtdNegativos DEFAULT (0);
    IF COL_LENGTH('dbo.AiTicketInsights', 'QtdPositivos') IS NULL
        ALTER TABLE dbo.AiTicketInsights ADD QtdPositivos INT NOT NULL CONSTRAINT DF_AiTicketInsights_QtdPositivos DEFAULT (0);
    IF COL_LENGTH('dbo.AiTicketInsights', 'RevisadoPor') IS NULL
        ALTER TABLE dbo.AiTicketInsights ADD RevisadoPor NVARCHAR(100) NULL;
    IF COL_LENGTH('dbo.AiTicketInsights', 'RevisadoEm') IS NULL
        ALTER TABLE dbo.AiTicketInsights ADD RevisadoEm DATETIME NULL;
    IF COL_LENGTH('dbo.AiTicketInsights', 'MotivoRevisao') IS NULL
        ALTER TABLE dbo.AiTicketInsights ADD MotivoRevisao NVARCHAR(300) NULL;
END
SELECT CASE WHEN COL_LENGTH('dbo.AiTicketInsights', 'StatusRevisao') IS NOT NULL
             AND COL_LENGTH('dbo.AiTicketInsights', 'QtdNegativos') IS NOT NULL
             AND COL_LENGTH('dbo.AiTicketInsights', 'QtdPositivos') IS NOT NULL
             AND COL_LENGTH('dbo.AiTicketInsights', 'RevisadoPor') IS NOT NULL
             AND COL_LENGTH('dbo.AiTicketInsights', 'RevisadoEm') IS NOT NULL
             AND COL_LENGTH('dbo.AiTicketInsights', 'MotivoRevisao') IS NOT NULL
            THEN 1 ELSE 0 END;";

    /// <summary>
    /// Casos que a IA pode usar para responder (tabela com apelido I).
    /// Só entra o que o cliente confirmou que resolveu ou o que um atendente aprovou na curadoria.
    /// </summary>
    public const string FiltroUsavel = @"
          ISNULL(LTRIM(RTRIM(I.Resumo)), '')  <> ''
          AND ISNULL(LTRIM(RTRIM(I.Solucao)), '') <> ''
          AND ISNULL(I.StatusRevisao, '') NOT IN ('descartado', 'suspenso')
          AND (I.SolucaoConfirmada = 1 OR I.StatusRevisao = 'aprovado')";

    /// <summary>Sem as colunas novas (sem permissão para criar): só o que o cliente confirmou.</summary>
    public const string FiltroUsavelSemColunas = @"
          ISNULL(LTRIM(RTRIM(I.Resumo)), '')  <> ''
          AND ISNULL(LTRIM(RTRIM(I.Solucao)), '') <> ''
          AND I.SolucaoConfirmada = 1";

    public static string Filtro(bool colunasOk) => colunasOk ? FiltroUsavel : FiltroUsavelSemColunas;

    /// <summary>
    /// Início da regra nova. Antes disso a IA já usava os casos com confiança >= 70 sem o cliente confirmar:
    /// esses antigos, sem nenhum 👎, são aprovados automaticamente uma vez (a IA mantém a base que já tinha).
    /// Casos criados depois disso (inclusive os regerados) passam pela revisão normal.
    /// </summary>
    public static readonly DateTime InicioCuradoria = new(2026, 10, 28, 8, 0, 0);

    private const string SqlAprovarAntigos = @"
UPDATE dbo.AiTicketInsights
   SET StatusRevisao = 'aprovado',
       RevisadoPor   = 'AUTOMATICO',
       RevisadoEm    = GETDATE(),
       MotivoRevisao = N'Aprovado automaticamente: já era usado pela IA antes da curadoria (confiança >= 70, sem 👎)'
 WHERE StatusRevisao IS NULL
   AND SolucaoConfirmada = 0
   AND ISNULL(Confianca, 0) >= 70
   AND QtdNegativos = 0
   AND ISNULL(LTRIM(RTRIM(Resumo)), '')  <> ''
   AND ISNULL(LTRIM(RTRIM(Solucao)), '') <> ''
   AND ISNULL(CriadoEm, '19000101') < @corte;";

    /// <summary>Roda junto com a verificação das colunas (uma vez por início do sistema). Não repete nada:
    /// só pega caso antigo que ninguém revisou.</summary>
    private static async Task AprovarAntigosAsync(SqlConnection conn)
    {
        try
        {
            await using var cmd = new SqlCommand(SqlAprovarAntigos, conn);
            cmd.Parameters.Add("@corte", System.Data.SqlDbType.DateTime).Value = InicioCuradoria;
            var n = await cmd.ExecuteNonQueryAsync();
            if (n > 0)
                LogHelper.Log($"[IA CURADORIA] {n} caso(s) antigo(s) aprovado(s) automaticamente (já eram usados pela IA, sem 👎).");
        }
        catch (Exception ex)
        {
            LogHelper.Log("[IA CURADORIA] Não aprovou os casos antigos: " + ex.Message);
        }
    }

    /// <summary>Cria as colunas se faltarem. Devolve true quando elas existem (só consulta o banco uma vez).</summary>
    public static async Task<bool> GarantirAsync(SqlConnection conn)
    {
        if (_verificado) return _colunasOk;

        await _trava.WaitAsync();
        try
        {
            if (_verificado) return _colunasOk;

            try
            {
                await using var cmd = new SqlCommand(Sql, conn);
                var r = await cmd.ExecuteScalarAsync();
                _colunasOk = r != null && r != DBNull.Value && Convert.ToInt32(r) == 1;
            }
            catch (Exception ex)
            {
                // Pode ser falta de permissão ou queda momentânea: tenta de novo na próxima vez
                LogHelper.Log("[IA CURADORIA] Não foi possível criar as colunas de revisão em AiTicketInsights " +
                              "(rode Scripts/2026-10-28_IA_Curadoria.sql). A IA usa só casos confirmados pelo cliente. " + ex.Message);
                return false;
            }

            if (!_colunasOk)
                LogHelper.Log("[IA CURADORIA] Colunas de revisão ausentes: curadoria e feedback dos casos desligados.");
            else
                await AprovarAntigosAsync(conn);

            _verificado = true;
            return _colunasOk;
        }
        finally
        {
            _trava.Release();
        }
    }
}
