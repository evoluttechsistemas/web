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

        /// <summary>Agendas do período mais as regras das repetições em andamento (ver CardService.GetAgendasPeriodoAsync).</summary>
        public Task<List<AgendaModel>> CarregarAgendasPeriodoAsync(int codEmp, DateTime de, DateTime ate) =>
            _cardService.GetAgendasPeriodoAsync(codEmp, de, ate);

        /// <summary>Agendas de ticket: pendentes sempre e resolvidas só do período.</summary>
        public async Task<List<TicketChamadoDModel>> CarregarAgendasTicketsAsync(bool podeVerTodas, string usuario,
            DateTime? de = null, DateTime? ate = null)
        {
            return podeVerTodas
                ? await _ticketService.BuscarAgendasTicketsAsync("TODOS", de, ate)
                : await _ticketService.BuscarAgendasTicketsAsync(usuario, de, ate);
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
                ResolvidoPor = d.AgendaResolvidaPor,
                ResolvidoEm = d.AgendaResolvidaEm,
                NomeCliente = $"Ticket #{d.CodTicketChamadoC}",
                AgendaPaiCodigo = d.CodTicketChamadoC
            };
        }

        // ─────────────────────────── Repetição ───────────────────────────
        // Agenda repetitiva = uma linha "mestre" (TipoRepeticao Sempre/PorTempo, sem AgendaPaiCodigo) que só
        // guarda a regra. Cada dia da repetição é uma ocorrência: virtual (montada aqui, não está no banco)
        // ou gravada (linha com AgendaPaiCodigo = mestre), que é criada quando alguém edita ou finaliza o dia.
        // O mestre não aparece na tela: o primeiro dia também é uma ocorrência.

        /// <summary>
        /// Dias que já passaram aparecem (pendentes/atrasados) a partir desta data. Antes dela o dia só existia se
        /// alguém tivesse aberto a tela de Agendas naquele dia; voltar mais atrás encheria a tela de atrasadas antigas.
        /// </summary>
        public static readonly DateTime InicioOcorrenciasPassadas = new(2026, 10, 30);

        public static bool EhMestreRepeticao(AgendaModel a) =>
            !(a.AgendaPaiCodigo > 0) &&
            !string.IsNullOrEmpty(a.TipoRepeticao) && a.TipoRepeticao != "UmaVez";

        /// <summary>Tela de Agendas: dias passados (desde <see cref="InicioOcorrenciasPassadas"/>) até 1 ano para frente.</summary>
        public List<AgendaModel> ExpandirAgendasRepetitivas(List<AgendaModel> agendasOriginais) =>
            ExpandirAgendas(agendasOriginais, InicioOcorrenciasPassadas, DateTime.Today.AddDays(364));

        /// <summary>
        /// Agendas do banco → lista para mostrar: agendas de uma vez, ocorrências gravadas (sem duplicidade)
        /// e as ocorrências virtuais das repetições entre <paramref name="inicio"/> e <paramref name="fim"/>.
        /// Cada repetição só gera dias a partir da sua data de início e até a data fim (PorTempo).
        /// </summary>
        public static List<AgendaModel> ExpandirAgendas(List<AgendaModel> agendasBanco, DateTime inicio, DateTime fim)
        {
            var resultado = new List<AgendaModel>();
            var mestres = new List<AgendaModel>();
            var ocorrencias = new List<AgendaModel>();

            foreach (var a in agendasBanco)
            {
                if (a.AgendaPaiCodigo > 0) ocorrencias.Add(a);
                else if (EhMestreRepeticao(a)) mestres.Add(a);
                else if (a.Status != "EXCLUIDO") resultado.Add(a);   // agenda de uma vez
            }

            // Mesmo dia gravado duas vezes (duas pessoas abriram a tela juntas antes desta versão):
            // mostra um só, o resolvido se algum foi resolvido
            var ocorrenciasUnicas = ocorrencias
                .GroupBy(o => (Pai: o.AgendaPaiCodigo!.Value, Dia: o.DataAgendamento.Date))
                .Select(g => g.OrderByDescending(o => o.Status == "EXCLUIDO")
                              .ThenByDescending(o => o.Status == "RESOLVIDO")
                              .ThenBy(o => o.Codigo).First())
                .ToList();
            // Dia excluído: fica gravado só para a repetição não mostrar ele de novo
            resultado.AddRange(ocorrenciasUnicas.Where(o => o.Status != "EXCLUIDO"));

            var gravadasPorMestre = ocorrenciasUnicas
                .GroupBy(o => o.AgendaPaiCodigo!.Value)
                .ToDictionary(g => g.Key, g => g.Select(o => o.DataAgendamento.Date).ToHashSet());

            foreach (var mestre in mestres)
            {
                // Mestre resolvido = série encerrada (antes desta versão, finalizar o 1º dia encerrava a série)
                if (mestre.Status == "RESOLVIDO") continue;

                var de = mestre.DataAgendamento.Date;
                if (de < InicioOcorrenciasPassadas) de = InicioOcorrenciasPassadas;
                if (de < inicio.Date) de = inicio.Date;

                var ate = fim.Date;
                if (mestre.TipoRepeticao == "PorTempo" && mestre.DataFimRepeticao.HasValue &&
                    mestre.DataFimRepeticao.Value.Date < ate)
                    ate = mestre.DataFimRepeticao.Value.Date;

                if (ate < de) continue;

                gravadasPorMestre.TryGetValue(mestre.Codigo, out var jaGravadas);
                foreach (var dia in DiasDaRepeticao(mestre, de, ate))
                    if (jaGravadas == null || !jaGravadas.Contains(dia))
                        resultado.Add(CriarOcorrenciaVirtual(mestre, dia));
            }

            return resultado
                .OrderBy(a => a.DataAgendamento)
                .ThenBy(a => a.HoraAgendamento)
                .ToList();
        }

        /// <summary>Dias da repetição entre <paramref name="de"/> e <paramref name="ate"/> (inclusive).</summary>
        private static IEnumerable<DateTime> DiasDaRepeticao(AgendaModel mestre, DateTime de, DateTime ate)
        {
            switch (mestre.FrequenciaRepeticao ?? "")
            {
                case "DIARIO":
                    for (var d = de; d <= ate; d = d.AddDays(1))
                        yield return d;
                    break;

                case "SEMANAL":
                    var dias = ParseDias(mestre.DiasRepeticao);
                    if (dias.Count == 0) dias.Add((int)mestre.DataAgendamento.DayOfWeek);   // sem dia marcado: o dia da semana do início
                    for (var d = de; d <= ate; d = d.AddDays(1))
                        if (dias.Contains((int)d.DayOfWeek))
                            yield return d;
                    break;

                case "MENSAL":
                    var diaDoMes = mestre.DataAgendamento.Day;
                    for (var mes = new DateTime(de.Year, de.Month, 1); mes <= ate; mes = mes.AddMonths(1))
                    {
                        var d = new DateTime(mes.Year, mes.Month, Math.Min(diaDoMes, DateTime.DaysInMonth(mes.Year, mes.Month)));
                        if (d >= de && d <= ate)
                            yield return d;
                    }
                    break;
            }
        }

        private static List<int> ParseDias(string? diasRepeticao) =>
            string.IsNullOrWhiteSpace(diasRepeticao)
                ? new List<int>()
                : diasRepeticao.Split(',').Where(d => int.TryParse(d, out _)).Select(int.Parse).ToList();

        public static AgendaModel CriarOcorrenciaVirtual(AgendaModel agendaMestre, DateTime data)
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
                Status = "AGENDADO",   // cada dia tem o seu status; o do mestre não vale para os dias
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

        /// <summary>Devolve false quando já existia uma agenda igual pendente (não grava duplicada).</summary>
        public async Task<bool> SalvarAgendaAsync(AgendaModel agenda, bool modoEdicao)
        {
            if (modoEdicao)
            {
                await _cardService.AtualizarAgendaAsync(agenda);
                return true;
            }
            return await _cardService.SalvarAgendaAsync(agenda);
        }

        /// <summary>
        /// Dia virtual de uma repetição que vai ser editado: grava o dia (ou pega o que já está gravado) e devolve
        /// a agenda desse dia. As alterações valem só para ele.
        /// </summary>
        public async Task<AgendaModel?> MaterializarOcorrenciaSeNecessarioAsync(
            AgendaModel agenda,
            List<AgendaModel> agendas)
        {
            if (!agenda.EhOcorrenciaGerada || !agenda.AgendaPaiCodigo.HasValue)
                return agenda;

            var agendaMestre = agendas.FirstOrDefault(a => a.Codigo == agenda.AgendaPaiCodigo.Value);
            if (agendaMestre == null)
                return null;

            var codigo = await _cardService.MaterializarOcorrenciaAgendaAsync(agendaMestre, agenda.DataAgendamento);

            var dia = CriarOcorrenciaVirtual(agendaMestre, agenda.DataAgendamento);
            dia.Codigo = codigo;
            dia.EhOcorrenciaGerada = false;
            dia.TipoRepeticao = null;          // a linha do dia não repete; quem repete é o mestre
            dia.FrequenciaRepeticao = null;
            dia.DataFimRepeticao = null;
            dia.DiasRepeticao = null;
            return dia;
        }

        /// <summary>Finaliza a agenda e devolve o código da linha finalizada (para desfazer), ou 0.</summary>
        public async Task<int> FinalizarAgendaAsync(AgendaModel agenda, List<AgendaModel> agendas, string? usuario = null)
        {
            // Agenda originada de ticket: apenas marca a agenda-detalhe como resolvida,
            // sem tocar no status do ticket pai.
            if (agenda.Origem == "TICKET")
            {
                if (agenda.Codigo <= 0) return 0;
                await _ticketService.FinalizarAgendaTicketAsync(agenda.Codigo);
                return agenda.Codigo;
            }

            // Dia virtual de uma repetição: grava o dia e resolve só ele (a série continua)
            if (agenda.EhOcorrenciaGerada && agenda.AgendaPaiCodigo.HasValue)
            {
                var agendaMestre = agendas.FirstOrDefault(a => a.Codigo == agenda.AgendaPaiCodigo.Value);
                if (agendaMestre == null)
                    return 0;

                var codigo = await _cardService.MaterializarOcorrenciaAgendaAsync(agendaMestre, agenda.DataAgendamento);
                await _cardService.ResolverAgendaAsync(codigo, usuario);
                return codigo;
            }

            // O mestre da repetição não é um dia: finalizar ele encerraria a série inteira
            if (EhMestreRepeticao(agenda))
                return 0;

            await _cardService.ResolverAgendaAsync(agenda.Codigo, usuario);
            return agenda.Codigo;
        }

        /// <summary>Volta para pendente uma agenda finalizada (desfazer / reabrir).</summary>
        public async Task ReabrirAgendaAsync(string? origem, int codigo)
        {
            if (codigo <= 0) return;
            if (origem == "TICKET")
                await _ticketService.ReabrirAgendaTicketAsync(codigo);
            else
                await _cardService.ReabrirAgendaAsync(codigo);
        }
    }
}