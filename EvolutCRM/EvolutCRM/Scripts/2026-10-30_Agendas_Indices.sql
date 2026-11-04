-- 2026-10-30 - Índices da agenda (desempenho)
-- Pode rodar em horário de pouco movimento: a criação lê a tabela TicketChamadoD uma vez.

-- Agendas de ticket: os avisos do menu (a cada 30s por usuário) e a tela de Agendas.
-- Índice filtrado: só as linhas que são agenda, então fica pequeno mesmo com a tabela enorme.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TicketChamadoD_Agenda' AND object_id = OBJECT_ID('dbo.TicketChamadoD'))
    CREATE NONCLUSTERED INDEX IX_TicketChamadoD_Agenda
        ON dbo.TicketChamadoD (CodEmp, AgendaResolvida, DataHoraAgenda)
        INCLUDE (CodTicketChamadoC, NotificarAgenda, UltimaNotificacaoAgenda, DataHora)
        WHERE NovaAgenda = 'S';

-- Agendas (CRM, Cliente, EvolutTech): carregar por período.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Agenda_CodEmp_Data' AND object_id = OBJECT_ID('dbo.Agenda'))
    CREATE NONCLUSTERED INDEX IX_Agenda_CodEmp_Data
        ON dbo.Agenda (CodEmp, DataAgendamento)
        INCLUDE (UsuarioUltimaGravacao, Status, AgendaPaiCodigo, TipoRepeticao);

-- Dias gravados de uma agenda repetitiva (não gravar o mesmo dia duas vezes).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Agenda_Pai' AND object_id = OBJECT_ID('dbo.Agenda'))
    CREATE NONCLUSTERED INDEX IX_Agenda_Pai
        ON dbo.Agenda (AgendaPaiCodigo, DataAgendamento)
        WHERE AgendaPaiCodigo IS NOT NULL;
