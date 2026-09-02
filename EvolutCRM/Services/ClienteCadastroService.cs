using EvolutCRM.Models;
using Microsoft.Data.SqlClient;
using System.Data;

namespace EvolutCRM.Services;

public class ClienteCadastroService
{
    private readonly string _conn;
    private readonly UserState _state;

    public ClienteCadastroService(IConfiguration cfg, UserState state)
    {
        _conn = cfg.GetConnectionString("Connection") ?? "";
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

    private string UsuarioAtual =>
        string.IsNullOrWhiteSpace(_state.CurrentUser)
            ? "SISTEMA"
            : _state.CurrentUser.Trim().ToUpper();

    private static string Limitar(string? valor, int tamanho)
    {
        var texto = (valor ?? "").Trim();
        return texto.Length > tamanho ? texto[..tamanho] : texto;
    }

    public async Task<List<ClienteCadastroModel>> ListarAsync(string termo = "", bool incluirInativos = false)
    {
        var lista = new List<ClienteCadastroModel>();

        await using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        var termoLimpo = (termo ?? "").Trim();

        await using var cmd = new SqlCommand(@"
SELECT TOP 200
    Codigo,
    CodEmp,
    ISNULL(Nome, '') AS Nome,
    ISNULL(Apelido, '') AS Apelido,
    ISNULL(Cpf, '') AS Cpf,
    ISNULL(Endereco, '') AS Endereco,
    ISNULL(NumeroEndereco, '') AS NumeroEndereco,
    ISNULL(Bairro, '') AS Bairro,
    ISNULL(NomeCidade, '') AS NomeCidade,
    ISNULL(UF, '') AS UF,
    ISNULL(CEP, '') AS CEP,
    ISNULL(ComplementoEndereco, '') AS ComplementoEndereco,
    ISNULL(Telefone, '') AS Telefone,
    ISNULL(Celular, '') AS Celular,
    ISNULL(Email, '') AS Email,
    ISNULL(Observacao, '') AS Observacao,
    ISNULL(Inativo, 'N') AS Inativo
FROM Cliente
WHERE CodEmp = @CodEmp
  AND (@IncluirInativos = 1 OR ISNULL(Inativo, 'N') <> 'S')
  AND (
        @Termo = ''
        OR Nome LIKE @Busca
        OR Apelido LIKE @Busca
        OR Cpf LIKE @Busca
        OR Celular LIKE @Busca
        OR Telefone LIKE @Busca
        OR CAST(Codigo AS VARCHAR(20)) = @Termo
      )
ORDER BY
    CASE WHEN ISNULL(Inativo, 'N') = 'S' THEN 1 ELSE 0 END,
    ISNULL(NULLIF(LTRIM(RTRIM(Apelido)), ''), ISNULL(Nome, ''))", conn);

        cmd.Parameters.AddWithValue("@CodEmp", CodEmpAtual);
        cmd.Parameters.AddWithValue("@IncluirInativos", incluirInativos ? 1 : 0);
        cmd.Parameters.AddWithValue("@Termo", termoLimpo);
        cmd.Parameters.AddWithValue("@Busca", "%" + termoLimpo + "%");

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            lista.Add(Mapear(rd));

        return lista;
    }

    public async Task<ClienteCadastroModel?> ObterAsync(int codigo)
    {
        await using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(@"
SELECT TOP 1
    Codigo,
    CodEmp,
    ISNULL(Nome, '') AS Nome,
    ISNULL(Apelido, '') AS Apelido,
    ISNULL(Cpf, '') AS Cpf,
    ISNULL(Endereco, '') AS Endereco,
    ISNULL(NumeroEndereco, '') AS NumeroEndereco,
    ISNULL(Bairro, '') AS Bairro,
    ISNULL(NomeCidade, '') AS NomeCidade,
    ISNULL(UF, '') AS UF,
    ISNULL(CEP, '') AS CEP,
    ISNULL(ComplementoEndereco, '') AS ComplementoEndereco,
    ISNULL(Telefone, '') AS Telefone,
    ISNULL(Celular, '') AS Celular,
    ISNULL(Email, '') AS Email,
    ISNULL(Observacao, '') AS Observacao,
    ISNULL(Inativo, 'N') AS Inativo
FROM Cliente
WHERE Codigo = @Codigo
  AND CodEmp = @CodEmp", conn);

        cmd.Parameters.AddWithValue("@Codigo", codigo);
        cmd.Parameters.AddWithValue("@CodEmp", CodEmpAtual);

        await using var rd = await cmd.ExecuteReaderAsync();
        return await rd.ReadAsync() ? Mapear(rd) : null;
    }

    public async Task<int> SalvarAsync(ClienteCadastroModel cliente)
    {
        if (cliente.Codigo > 0)
        {
            await AtualizarAsync(cliente);
            return cliente.Codigo;
        }

        await using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(@"
DECLARE @NovoCliente TABLE (Codigo INT);

INSERT INTO Cliente
(
    CodEmp,
    Cpf, Nome, Apelido,
    Endereco, NumeroEndereco, Bairro, NomeCidade, UF, CEP, ComplementoEndereco,
    Telefone, Celular, Email, Observacao,
    TipoCliente, ContribuinteICMS, Inativo,
    DataHoraUltimaGravacao, UsuarioUltimaGravacao,
    FisicaJuridica, ClienteMensalista,
    BloqueiaVendaCrediario, EnviaForcaVendas, AcrescimoCRD, EmiteBoleto
)
OUTPUT INSERTED.Codigo INTO @NovoCliente
VALUES
(
    @CodEmp,
    @Cpf, @Nome, @Apelido,
    @Endereco, @NumeroEndereco, @Bairro, @NomeCidade, @UF, @CEP, @ComplementoEndereco,
    @Telefone, @Celular, @Email, @Observacao,
    'NORMAL', 'N', @Inativo,
    GETDATE(), @Usuario,
    @FisicaJuridica, 'N',
    'N', 'N', 'N', 'N'
);

SELECT Codigo FROM @NovoCliente;", conn);

        PreencherParametros(cmd, cliente);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task AtualizarAsync(ClienteCadastroModel cliente)
    {
        await using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(@"
UPDATE Cliente
SET
    Cpf = @Cpf,
    Nome = @Nome,
    Apelido = @Apelido,
    Endereco = @Endereco,
    NumeroEndereco = @NumeroEndereco,
    Bairro = @Bairro,
    NomeCidade = @NomeCidade,
    UF = @UF,
    CEP = @CEP,
    ComplementoEndereco = @ComplementoEndereco,
    Telefone = @Telefone,
    Celular = @Celular,
    Email = @Email,
    Observacao = @Observacao,
    Inativo = @Inativo,
    FisicaJuridica = @FisicaJuridica,
    DataHoraUltimaGravacao = GETDATE(),
    UsuarioUltimaGravacao = @Usuario
WHERE Codigo = @Codigo
  AND CodEmp = @CodEmp", conn);

        cmd.Parameters.AddWithValue("@Codigo", cliente.Codigo);
        PreencherParametros(cmd, cliente);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ExcluirAsync(int codigo)
    {
        await using var conn = new SqlConnection(_conn);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(@"
UPDATE Cliente
SET
    Inativo = 'S',
    DataHoraUltimaGravacao = GETDATE(),
    UsuarioUltimaGravacao = @Usuario
WHERE Codigo = @Codigo
  AND CodEmp = @CodEmp", conn);

        cmd.Parameters.AddWithValue("@Codigo", codigo);
        cmd.Parameters.AddWithValue("@CodEmp", CodEmpAtual);
        cmd.Parameters.AddWithValue("@Usuario", UsuarioAtual);

        await cmd.ExecuteNonQueryAsync();
    }

    private void PreencherParametros(SqlCommand cmd, ClienteCadastroModel cliente)
    {
        cmd.Parameters.AddWithValue("@CodEmp", CodEmpAtual);
        cmd.Parameters.AddWithValue("@Cpf", Limitar(cliente.CpfCnpj, 20));
        cmd.Parameters.AddWithValue("@Nome", Limitar(cliente.Nome, 150));
        cmd.Parameters.AddWithValue("@Apelido", Limitar(cliente.Apelido, 80));
        cmd.Parameters.AddWithValue("@Endereco", Limitar(cliente.Endereco, 150));
        cmd.Parameters.AddWithValue("@NumeroEndereco", Limitar(cliente.NumeroEndereco, 20));
        cmd.Parameters.AddWithValue("@Bairro", Limitar(cliente.Bairro, 80));
        cmd.Parameters.AddWithValue("@NomeCidade", Limitar(cliente.NomeCidade, 80));
        cmd.Parameters.AddWithValue("@UF", Limitar(cliente.UF, 2).ToUpperInvariant());
        cmd.Parameters.AddWithValue("@CEP", Limitar(cliente.CEP, 10));
        cmd.Parameters.AddWithValue("@ComplementoEndereco", Limitar(cliente.ComplementoEndereco, 120));
        cmd.Parameters.AddWithValue("@Telefone", Limitar(cliente.Telefone, 30));
        cmd.Parameters.AddWithValue("@Celular", Limitar(cliente.Celular, 30));
        cmd.Parameters.AddWithValue("@Email", Limitar(cliente.Email, 120));
        cmd.Parameters.AddWithValue("@Observacao", Limitar(cliente.Observacao, 500));
        cmd.Parameters.AddWithValue("@Inativo", cliente.Inativo ? "S" : "N");
        cmd.Parameters.AddWithValue("@FisicaJuridica", cliente.FisicaJuridica);
        cmd.Parameters.AddWithValue("@Usuario", UsuarioAtual);
    }

    private static ClienteCadastroModel Mapear(SqlDataReader rd)
    {
        return new ClienteCadastroModel
        {
            Codigo = Convert.ToInt32(rd["Codigo"]),
            CodEmp = Convert.ToInt32(rd["CodEmp"]),
            Nome = rd["Nome"]?.ToString() ?? "",
            Apelido = rd["Apelido"]?.ToString() ?? "",
            CpfCnpj = rd["Cpf"]?.ToString() ?? "",
            Endereco = rd["Endereco"]?.ToString() ?? "",
            NumeroEndereco = rd["NumeroEndereco"]?.ToString() ?? "",
            Bairro = rd["Bairro"]?.ToString() ?? "",
            NomeCidade = rd["NomeCidade"]?.ToString() ?? "",
            UF = rd["UF"]?.ToString() ?? "",
            CEP = rd["CEP"]?.ToString() ?? "",
            ComplementoEndereco = rd["ComplementoEndereco"]?.ToString() ?? "",
            Telefone = rd["Telefone"]?.ToString() ?? "",
            Celular = rd["Celular"]?.ToString() ?? "",
            Email = rd["Email"]?.ToString() ?? "",
            Observacao = rd["Observacao"]?.ToString() ?? "",
            Inativo = string.Equals(rd["Inativo"]?.ToString(), "S", StringComparison.OrdinalIgnoreCase)
        };
    }
}
