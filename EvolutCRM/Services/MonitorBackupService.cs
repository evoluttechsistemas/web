using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace EvolutCRM.Services
{
    public class MonitorBackupModel
    {
        public int CodCliente { get; set; }
        public string Cnpj { get; set; } = "";
        public string NomeCliente { get; set; } = "";
        public string Apelido { get; set; } = "";
        public DateTime? DataHoraUltimaSincronizacao { get; set; }
        public string Arquivo { get; set; } = "";
        public long TamanhoBytes { get; set; }
        public string Computador { get; set; } = "";
        public string VersaoSistema { get; set; } = "";
        public int QuantidadeSincronizacoes { get; set; }
        public StatusBackup Status { get; set; }
    }

    public class ClienteSemBackupModel
    {
        public int CodCliente { get; set; }
        public string NomeCliente { get; set; } = "";
        public string Apelido { get; set; } = "";
        public string Cnpj { get; set; } = "";
    }

    public enum StatusBackup
    {
        EmCurso,
        Ok,
        Atencao,
        Critico,
        SemBackup
    }

    public class MonitorBackupService
    {
        private readonly string _conn;
        private readonly UserState _state;

        public MonitorBackupService(IConfiguration config, UserState state)
        {
            _conn = config.GetConnectionString("Connection")!;
            _state = state;
        }

        private int CodEmpAtual
        {
            get
            {
                if (_state.CurrentCompanyId <= 0)
                    throw new InvalidOperationException("Empresa do usuário não carregada no UserState.");

                return _state.CurrentCompanyId;
            }
        }

        public async Task<List<MonitorBackupModel>> ObterStatusBackupsAsync()
        {
            var lista = new List<MonitorBackupModel>();
            var agora = DateTime.Now;

            const string sql = @"
    SELECT
        b.CodCliente,
        b.Cnpj,
        b.NomeCliente,
        ISNULL(c.Apelido, '')               AS Apelido,
        MAX(b.DataHoraUltimaSincronizacao) AS DataHoraUltimaSincronizacao,
        MAX(b.Arquivo)                     AS Arquivo,
        MAX(b.TamanhoBytes)                AS TamanhoBytes,
        MAX(b.Computador)                  AS Computador,
        MAX(b.VersaoSistema)               AS VersaoSistema,
        SUM(b.QuantidadeSincronizacoes)    AS QuantidadeSincronizacoes
    FROM ControleBackupCliente b
    INNER JOIN Cliente c
            ON c.CodEmp = b.CodEmp
           AND (
                REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(c.Cnpj)), '.', ''), '/', ''), '-', '')
                    = REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(b.Cnpj)), '.', ''), '/', ''), '-', '')
                OR REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(c.Cpf)), '.', ''), '/', ''), '-', '')
                    = REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(b.Cnpj)), '.', ''), '/', ''), '-', '')
           )
    WHERE b.CodEmp = @CodEmp
      AND c.CodEmp = @CodEmp
      AND c.ClienteMensalista = 'S'
    GROUP BY b.CodCliente, b.Cnpj, b.NomeCliente, c.Apelido
    ORDER BY MAX(b.DataHoraUltimaSincronizacao) ASC";

            await using var con = new SqlConnection(_conn);
            await using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@CodEmp", CodEmpAtual);

            await con.OpenAsync();
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                DateTime? ultima = rd.IsDBNull("DataHoraUltimaSincronizacao")
                    ? null
                    : rd.GetDateTime("DataHoraUltimaSincronizacao");

                lista.Add(new MonitorBackupModel
                {
                    CodCliente = rd.IsDBNull("CodCliente")
                        ? 0
                        : int.TryParse(rd["CodCliente"]?.ToString(), out var cod) ? cod : 0,
                    Cnpj = rd.IsDBNull("Cnpj") ? "" : rd.GetString("Cnpj"),
                    NomeCliente = rd.IsDBNull("NomeCliente") ? "" : rd.GetString("NomeCliente"),
                    Apelido = rd.IsDBNull("Apelido") ? "" : rd.GetString("Apelido"),
                    DataHoraUltimaSincronizacao = ultima,
                    Arquivo = rd.IsDBNull("Arquivo") ? "" : rd.GetString("Arquivo"),
                    TamanhoBytes = rd.IsDBNull("TamanhoBytes") ? 0 : rd.GetInt64("TamanhoBytes"),
                    Computador = rd.IsDBNull("Computador") ? "" : rd.GetString("Computador"),
                    VersaoSistema = rd.IsDBNull("VersaoSistema") ? "" : rd["VersaoSistema"]?.ToString() ?? "",
                    QuantidadeSincronizacoes = rd.IsDBNull("QuantidadeSincronizacoes") ? 0 : Convert.ToInt32(rd["QuantidadeSincronizacoes"]),
                    Status = ClassificarStatus(ultima, agora)
                });
            }

            return lista;
        }

        public static StatusBackup ClassificarStatus(DateTime? ultima, DateTime agora)
        {
            if (ultima == null) return StatusBackup.SemBackup;
            var diff = agora - ultima.Value;
            if (diff.TotalMinutes <= 5) return StatusBackup.EmCurso;
            if (diff.TotalHours <= 24) return StatusBackup.Ok;
            if (diff.TotalDays <= 2) return StatusBackup.Atencao;
            if (diff.TotalDays <= 5) return StatusBackup.Critico;
            return StatusBackup.SemBackup;
        }

        public async Task<List<MonitorBackupModel>> ObterClientesParaAlertaAsync()
        {
            var todos = await ObterStatusBackupsAsync();
            return todos
                .Where(x => x.Status == StatusBackup.Critico || x.Status == StatusBackup.SemBackup)
                .ToList();
        }

        public async Task<List<ClienteSemBackupModel>> ObterClientesSemBackupAsync()
        {
            var lista = new List<ClienteSemBackupModel>();

            const string sql = @"
SELECT
    c.Codigo                         AS CodCliente,
    c.Nome                           AS NomeCliente,
    c.Apelido                        AS Apelido,
    ISNULL(NULLIF(c.Cnpj,''), c.Cpf) AS Cnpj
FROM Cliente c
WHERE c.CodEmp = @CodEmp
  AND c.ClienteMensalista = 'S'
  AND NOT EXISTS (
      SELECT 1
      FROM ControleBackupCliente b
      WHERE b.CodEmp = @CodEmp
        AND b.CodCliente = c.Codigo
  )
ORDER BY c.Nome ASC";

            await using var con = new SqlConnection(_conn);
            await using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@CodEmp", CodEmpAtual);

            await con.OpenAsync();
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new ClienteSemBackupModel
                {
                    CodCliente = rd.IsDBNull("CodCliente") ? 0 : rd.GetInt32("CodCliente"),
                    NomeCliente = rd.IsDBNull("NomeCliente") ? "" : rd.GetString("NomeCliente"),
                    Apelido = rd.IsDBNull("Apelido") ? "" : rd.GetString("Apelido"),
                    Cnpj = rd.IsDBNull("Cnpj") ? "" : rd.GetString("Cnpj"),
                });
            }

            return lista;
        }
    }
}
