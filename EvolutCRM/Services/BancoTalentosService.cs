using EvolutCRM.Models;

namespace EvolutCRM.Services;

public class BancoTalentosService
{
    private readonly UserState _userState;
    private readonly List<CandidatoTalentoModel> _candidatos = new();
    private int _proximoId = 1;

    public BancoTalentosService(UserState userState)
    {
        _userState = userState;
    }

    private int CodEmpAtual
    {
        get
        {
            if (_userState.CurrentCompanyId <= 0)
                throw new InvalidOperationException("Empresa do usuário não carregada no UserState.");

            return _userState.CurrentCompanyId;
        }
    }

    public Task<List<CandidatoTalentoModel>> ListarAsync()
    {
        var codEmp = CodEmpAtual;

        var lista = _candidatos
            .Where(c => c.CodEmp == codEmp)
            .OrderByDescending(c => c.DataCadastro)
            .ToList();

        return Task.FromResult(lista);
    }

    public Task<CandidatoTalentoModel?> ObterAsync(int id)
    {
        var codEmp = CodEmpAtual;

        var candidato = _candidatos
            .FirstOrDefault(c => c.Id == id && c.CodEmp == codEmp);

        return Task.FromResult(candidato);
    }

    public Task SalvarAsync(CandidatoTalentoModel candidato)
    {
        candidato.Id = _proximoId++;
        candidato.CodEmp = CodEmpAtual;
        candidato.DataCadastro = DateTime.Now;
        candidato.Status = string.IsNullOrWhiteSpace(candidato.Status)
            ? "Novo"
            : candidato.Status;

        _candidatos.Add(candidato);
        return Task.CompletedTask;
    }

    public Task AtualizarStatusAsync(int id, string status)
    {
        var codEmp = CodEmpAtual;

        var candidato = _candidatos
            .FirstOrDefault(c => c.Id == id && c.CodEmp == codEmp);

        if (candidato is not null)
            candidato.Status = status;

        return Task.CompletedTask;
    }
}