using EvolutCRM.Models;

namespace EvolutCRM.Services
{
    public class AgendaService
    {
        private readonly CardService _cardService;
        private readonly TicketService _ticketService;

        public AgendaService(CardService cardService, TicketService ticketService)
        {
            _cardService = cardService;
            _ticketService = ticketService;
        }

        public async Task<List<AgendaModel>> CarregarAgendasAsync(int codEmp)
        {
            return await _cardService.GetAgendasEmpresaAsync(codEmp);
        }

        public async Task<List<TicketChamadoDModel>> CarregarAgendasTicketsAsync(bool podeVerTodas, string usuario)
        {
            return podeVerTodas
                ? await _ticketService.BuscarAgendasTicketsAsync("TODOS")
                : await _ticketService.BuscarAgendasTicketsAsync(usuario);
        }

        public List<AgendaModel> MontarTodasAgendasTela(
            List<AgendaModel> agendasExpandidas,
            List<TicketChamadoDModel> agendasTickets)
        {
            return agendasExpandidas
                .Concat(agendasTickets.Select(ConverterAgendaTicket))
                .ToList();
        }

        public AgendaModel ConverterAgendaTicket(TicketChamadoDModel d)
        {
            var dataHora = d.DataHoraAgenda ?? DateTime.Now;

            return new AgendaModel
            {
                Codigo = d.Codigo,
                Origem = "TICKET",
                Descricao = $"Ticket #{d.CodTicketChamadoC} - {d.Anotacao}",
                DataAgendamento = dataHora.Date,
                HoraAgendamento = dataHora.TimeOfDay,
                Usuario = d.Usuario,
                Status = d.AgendaResolvida == "S" ? "RESOLVIDO" : "PENDENTE",
                NomeCliente = $"Ticket #{d.CodTicketChamadoC}",
                AgendaPaiCodigo = d.CodTicketChamadoC
            };
        }

        public List<AgendaModel> ExpandirAgendasRepetitivas(List<AgendaModel> agendasOriginais)
        {
            var agendasSemDuplicatas = agendasOriginais
                .GroupBy(a => new { a.Codigo, Data = a.DataAgendamento.Date, a.Usuario })
                .Select(g => g.First())
                .ToList();

            var resultado = new List<AgendaModel>();

            var dataInicio = DateTime.Today;
            var dataFim = DateTime.Today.AddDays(364);

            var agendasMestras = agendasSemDuplicatas
                .Where(a => !a.AgendaPaiCodigo.HasValue)
                .ToList();

            var ocorrenciasMaterializadas = agendasSemDuplicatas
                .Where(a => a.AgendaPaiCodigo.HasValue && a.AgendaPaiCodigo > 0)
                .ToList();

            foreach (var agenda in agendasMestras.Where(a =>
                         string.IsNullOrEmpty(a.TipoRepeticao) ||
                         a.TipoRepeticao == "UmaVez"))
            {
                resultado.Add(agenda);
            }

            foreach (var agendaMestre in agendasMestras.Where(a =>
                         !string.IsNullOrEmpty(a.TipoRepeticao) &&
                         a.TipoRepeticao != "UmaVez"))
            {
                var datasJaMaterializadas = ocorrenciasMaterializadas
                    .Where(a => a.AgendaPaiCodigo == agendaMestre.Codigo)
                    .Select(a => a.DataAgendamento.Date)
                    .ToHashSet();

                var datasGerar = new List<DateTime>();

                DateTime dataLimite = dataFim;

                if (agendaMestre.TipoRepeticao == "PorTempo" &&
                    agendaMestre.DataFimRepeticao.HasValue)
                {
                    dataLimite = agendaMestre.DataFimRepeticao.Value < dataFim
                        ? agendaMestre.DataFimRepeticao.Value
                        : dataFim;
                }

                switch (agendaMestre.FrequenciaRepeticao ?? "")
                {
                    case "DIARIO":
                        for (var data = dataInicio; data <= dataLimite; data = data.AddDays(1))
                        {
                            if (!datasJaMaterializadas.Contains(data.Date))
                                datasGerar.Add(data);
                        }
                        break;

                    case "SEMANAL":
                        var diasSelecionados = ParseDiasSemana(agendaMestre.DiasRepeticao);

                        for (var data = dataInicio; data <= dataLimite; data = data.AddDays(1))
                        {
                            if (diasSelecionados.Contains((int)data.DayOfWeek) &&
                                !datasJaMaterializadas.Contains(data.Date))
                            {
                                datasGerar.Add(data);
                            }
                        }
                        break;

                    case "MENSAL":
                        var diaDoMes = agendaMestre.DataAgendamento.Day;
                        var dataMensal = new DateTime(dataInicio.Year, dataInicio.Month, 1);

                        while (dataMensal <= dataLimite)
                        {
                            var diaMax = DateTime.DaysInMonth(dataMensal.Year, dataMensal.Month);
                            var dataOcorrencia = new DateTime(
                                dataMensal.Year,
                                dataMensal.Month,
                                Math.Min(diaDoMes, diaMax)
                            );

                            if (dataOcorrencia >= dataInicio &&
                                dataOcorrencia <= dataLimite &&
                                !datasJaMaterializadas.Contains(dataOcorrencia.Date))
                            {
                                datasGerar.Add(dataOcorrencia);
                            }

                            dataMensal = dataMensal.AddMonths(1);
                        }
                        break;
                }

                resultado.Add(agendaMestre);

                foreach (var data in datasGerar)
                    resultado.Add(CriarOcorrenciaVirtual(agendaMestre, data));
            }

            resultado.AddRange(ocorrenciasMaterializadas);

            return resultado
                .OrderBy(a => a.DataAgendamento)
                .ThenBy(a => a.HoraAgendamento)
                .ToList();
        }

        public AgendaModel CriarOcorrenciaVirtual(AgendaModel agendaMestre, DateTime data)
        {
            return new AgendaModel
            {
                Codigo = agendaMestre.Codigo,
                Origem = agendaMestre.Origem,
                CodCRMC = agendaMestre.CodCRMC,
                Descricao = agendaMestre.Descricao,
                DataAgendamento = data,
                HoraAgendamento = agendaMestre.HoraAgendamento,
                CodCliente = agendaMestre.CodCliente,
                NomeCliente = agendaMestre.NomeCliente,
                ApelidoCliente = agendaMestre.ApelidoCliente,
                EmailCliente = agendaMestre.EmailCliente,
                Usuario = agendaMestre.Usuario,
                Status = agendaMestre.Status,
                CodEmp = agendaMestre.CodEmp,
                TipoRepeticao = agendaMestre.TipoRepeticao,
                FrequenciaRepeticao = agendaMestre.FrequenciaRepeticao,
                DataFimRepeticao = agendaMestre.DataFimRepeticao,
                DiasRepeticao = agendaMestre.DiasRepeticao,
                EhOcorrenciaGerada = true,
                NumeroTelefone = agendaMestre.NumeroTelefone,
                AgendaPaiCodigo = agendaMestre.Codigo
            };
        }

        public List<int> ParseDiasSemana(string? diasRepeticao)
        {
            if (string.IsNullOrWhiteSpace(diasRepeticao))
                return new List<int>();

            return diasRepeticao
                .Split(',')
                .Where(d => int.TryParse(d, out _))
                .Select(int.Parse)
                .ToList();
        }

        public string? ValidarConflitoAgenda(
            AgendaModel agendaNova,
            List<AgendaModel> todasAgendasTela,
            bool modoEdicao)
        {
            var dataHoraNova = agendaNova.DataAgendamento.Date.Add(agendaNova.HoraAgendamento);
            var usuarioNovo = (agendaNova.Usuario ?? "").Trim().ToUpper();

            var conflito = todasAgendasTela
                .Where(a =>
                    a.Status != "RESOLVIDO"
                    && (a.Usuario ?? "").Trim().ToUpper() == usuarioNovo
                    && a.DataAgendamento.Date == agendaNova.DataAgendamento.Date
                    && (!modoEdicao || a.Codigo != agendaNova.Codigo || a.Origem == "TICKET")
                )
                .Select(a => new
                {
                    DataHora = a.DataAgendamento.Date.Add(a.HoraAgendamento),
                    Diferenca = Math.Abs((a.DataAgendamento.Date.Add(a.HoraAgendamento) - dataHoraNova).TotalMinutes)
                })
                .Where(x => x.Diferenca < 30)
                .OrderBy(x => x.Diferenca)
                .FirstOrDefault();

            if (conflito == null)
                return null;

            return $"Já existe agenda para {agendaNova.Usuario} às {conflito.DataHora:dd/MM/yyyy HH:mm}. " +
                   "É necessário manter pelo menos 30 minutos de diferença.";
        }

        public async Task SalvarAgendaAsync(AgendaModel agenda, bool modoEdicao)
        {
            if (modoEdicao)
                await _cardService.AtualizarAgendaAsync(agenda);
            else
                await _cardService.SalvarAgendaAsync(agenda);
        }

        public async Task<List<AgendaModel>> MaterializarOcorrenciasHojeAsync(
            List<AgendaModel> agendas,
            int codEmp)
        {
            var hoje = DateTime.Today;

            var agendasRepetitivas = agendas
                .Where(a =>
                    !string.IsNullOrEmpty(a.TipoRepeticao) &&
                    a.TipoRepeticao != "UmaVez" &&
                    !a.AgendaPaiCodigo.HasValue &&
                    a.DataAgendamento < hoje)
                .ToList();

            foreach (var agendaMestre in agendasRepetitivas)
            {
                var jaExisteHoje = agendas.Any(a =>
                    a.AgendaPaiCodigo == agendaMestre.Codigo &&
                    a.DataAgendamento.Date == hoje);

                if (jaExisteHoje)
                    continue;

                bool deveGerarHoje = agendaMestre.FrequenciaRepeticao switch
                {
                    "DIARIO" => true,
                    "SEMANAL" => ParseDiasSemana(agendaMestre.DiasRepeticao).Contains((int)hoje.DayOfWeek),
                    "MENSAL" => hoje.Day == agendaMestre.DataAgendamento.Day,
                    _ => false
                };

                if (agendaMestre.TipoRepeticao == "PorTempo" &&
                    agendaMestre.DataFimRepeticao.HasValue &&
                    hoje > agendaMestre.DataFimRepeticao.Value)
                {
                    deveGerarHoje = false;
                }

                if (deveGerarHoje)
                    await _cardService.MaterializarOcorrenciaAgendaAsync(agendaMestre, hoje);
            }

            return await _cardService.GetAgendasEmpresaAsync(codEmp);
        }

        public async Task<AgendaModel?> MaterializarOcorrenciaSeNecessarioAsync(
            AgendaModel agenda,
            List<AgendaModel> agendas)
        {
            if (!agenda.EhOcorrenciaGerada || !agenda.AgendaPaiCodigo.HasValue)
                return agenda;

            var agendaMestre = agendas.FirstOrDefault(a => a.Codigo == agenda.AgendaPaiCodigo.Value);

            if (agendaMestre == null)
                return null;

            await _cardService.MaterializarOcorrenciaAgendaAsync(
                agendaMestre,
                agenda.DataAgendamento
            );

            var agendasAtualizadas = await _cardService.GetAgendasEmpresaAsync(agenda.CodEmp);

            return agendasAtualizadas.FirstOrDefault(a =>
                a.AgendaPaiCodigo == agenda.AgendaPaiCodigo &&
                a.DataAgendamento.Date == agenda.DataAgendamento.Date);
        }

        public async Task FinalizarAgendaAsync(AgendaModel agenda, List<AgendaModel> agendas)
        {
            if (agenda.Origem == "TICKET")
                return;

            if (agenda.EhOcorrenciaGerada && agenda.AgendaPaiCodigo.HasValue)
            {
                var agendaMestre = agendas.FirstOrDefault(a => a.Codigo == agenda.AgendaPaiCodigo.Value);

                if (agendaMestre == null)
                    return;

                await _cardService.MaterializarOcorrenciaAgendaAsync(
                    agendaMestre,
                    agenda.DataAgendamento
                );

                var agendasAtualizadas = await _cardService.GetAgendasEmpresaAsync(agenda.CodEmp);

                agenda = agendasAtualizadas.FirstOrDefault(a =>
                    a.AgendaPaiCodigo == agenda.AgendaPaiCodigo &&
                    a.DataAgendamento.Date == agenda.DataAgendamento.Date);

                if (agenda == null)
                    return;
            }

            agenda.Status = "RESOLVIDO";
            await _cardService.AtualizarAgendaAsync(agenda);
        }
    }
}