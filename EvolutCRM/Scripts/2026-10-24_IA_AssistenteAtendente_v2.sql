-- =====================================================================
-- IA - Assistente: também no Card (comercial) e nas listas (Tickets / Funil)
-- Rodar DEPOIS do 2026-10-24_IA_AssistenteAtendente.sql. Pode rodar mais de uma vez.
-- Sem este script o assistente funciona, mas só o ticket guarda histórico.
-- =====================================================================

-- Origem da conversa:
--   ticket    = dentro do ticket (CodTicketChamadoC)
--   card      = dentro do card do comercial (CodCard)
--   g-suporte = lista de Tickets, sem atendimento aberto (por usuário)
--   g-comerc  = Funil comercial, sem card aberto (por usuário)
IF COL_LENGTH('dbo.TicketAssistenteIA', 'Origem') IS NULL
    ALTER TABLE dbo.TicketAssistenteIA
        ADD Origem VARCHAR(10) NOT NULL CONSTRAINT DF_TicketAssistenteIA_Origem DEFAULT ('ticket');
GO

IF COL_LENGTH('dbo.TicketAssistenteIA', 'CodCard') IS NULL
    ALTER TABLE dbo.TicketAssistenteIA ADD CodCard INT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TicketAssistenteIA_Card' AND object_id = OBJECT_ID('dbo.TicketAssistenteIA'))
    CREATE INDEX IX_TicketAssistenteIA_Card ON dbo.TicketAssistenteIA (Origem, CodCard, Arquivado, CodTicketAssistenteIA);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TicketAssistenteIA_Usuario' AND object_id = OBJECT_ID('dbo.TicketAssistenteIA'))
    CREATE INDEX IX_TicketAssistenteIA_Usuario ON dbo.TicketAssistenteIA (Origem, Usuario, Arquivado, CodTicketAssistenteIA);
GO

-- O que o comercial mais pergunta (para saber o que documentar / o que o sistema não tem):
-- SELECT TOP 100 Origem, CodCard, Usuario, Mensagem, DataHora
--   FROM TicketAssistenteIA WHERE Papel = 'atendente' AND Origem IN ('card', 'g-comerc') ORDER BY DataHora DESC;
