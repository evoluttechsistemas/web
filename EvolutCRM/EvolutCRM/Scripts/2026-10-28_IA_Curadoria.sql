-- =====================================================================
-- IA - Curadoria dos casos aprendidos (AiTicketInsights)
-- O sistema cria estas colunas sozinho no primeiro uso (IaCuradoriaSchema).
-- Rode este script só se o usuário do banco da aplicação não tiver permissão de ALTER TABLE.
-- Pode rodar mais de uma vez.
--
-- O que muda:
--  * A IA passa a usar só casos com solução confirmada pelo cliente (SolucaoConfirmada = 1)
--    ou aprovados por um atendente na tela Configurações › Curadoria da IA.
--  * Casos com 2 ou mais 👎 nas sugestões ficam suspensos até alguém revisar.
--  * Casos que geram sugestões enviadas quase iguais (similaridade >= 80) ganham peso na busca.
-- =====================================================================

IF COL_LENGTH('dbo.AiTicketInsights', 'StatusRevisao') IS NULL
    ALTER TABLE dbo.AiTicketInsights ADD StatusRevisao VARCHAR(20) NULL;   -- NULL/pendente | aprovado | descartado | suspenso
GO
IF COL_LENGTH('dbo.AiTicketInsights', 'QtdNegativos') IS NULL
    ALTER TABLE dbo.AiTicketInsights ADD QtdNegativos INT NOT NULL CONSTRAINT DF_AiTicketInsights_QtdNegativos DEFAULT (0);
GO
IF COL_LENGTH('dbo.AiTicketInsights', 'QtdPositivos') IS NULL
    ALTER TABLE dbo.AiTicketInsights ADD QtdPositivos INT NOT NULL CONSTRAINT DF_AiTicketInsights_QtdPositivos DEFAULT (0);
GO
IF COL_LENGTH('dbo.AiTicketInsights', 'RevisadoPor') IS NULL
    ALTER TABLE dbo.AiTicketInsights ADD RevisadoPor NVARCHAR(100) NULL;
GO
IF COL_LENGTH('dbo.AiTicketInsights', 'RevisadoEm') IS NULL
    ALTER TABLE dbo.AiTicketInsights ADD RevisadoEm DATETIME NULL;
GO
IF COL_LENGTH('dbo.AiTicketInsights', 'MotivoRevisao') IS NULL
    ALTER TABLE dbo.AiTicketInsights ADD MotivoRevisao NVARCHAR(300) NULL;
GO

-- Consultas úteis
-- Fila de revisão:
-- SELECT CodTicketChamadoC, Resumo, Solucao, Confianca FROM dbo.AiTicketInsights
--  WHERE ISNULL(StatusRevisao,'') IN ('','pendente') AND SolucaoConfirmada = 0 AND ISNULL(Solucao,'') <> '';
-- Suspensos por 👎:
-- SELECT CodTicketChamadoC, Resumo, QtdNegativos FROM dbo.AiTicketInsights WHERE StatusRevisao = 'suspenso';
