-- =====================================================================
-- IA - Passo 7: log das sugestões da IA (aproveitamento, feedback e avaliação)
-- Rodar ANTES de publicar. Pode rodar mais de uma vez.
-- (Sem a tabela a sugestão continua funcionando, só não mede nada.)
-- =====================================================================

IF OBJECT_ID('dbo.AiSugestaoLog', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AiSugestaoLog
    (
        CodAiSugestaoLog   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AiSugestaoLog PRIMARY KEY,
        CodTicketChamadoC  INT            NULL,
        CodEmp             INT            NULL,
        Usuario            NVARCHAR(100)  NULL,
        Origem             VARCHAR(20)    NOT NULL CONSTRAINT DF_AiSugestaoLog_Origem DEFAULT ('ticket'),  -- ticket | avaliacao
        DataHora           DATETIME       NOT NULL CONSTRAINT DF_AiSugestaoLog_DataHora DEFAULT (GETDATE()),

        Pergunta           NVARCHAR(MAX)  NULL,   -- dúvida entendida pela IA
        RespostaIA         NVARCHAR(MAX)  NULL,   -- o que a IA sugeriu
        Confianca          VARCHAR(10)    NULL,   -- alta | media | baixa
        Nivel              VARCHAR(10)    NULL,   -- ALTO | BAIXO (força da busca)
        MelhorScore        DECIMAL(6,3)   NULL,
        Fontes             NVARCHAR(MAX)  NULL,   -- JSON com docs/casos usados
        Modelo             VARCHAR(60)    NULL,
        DuracaoMs          INT            NULL,

        RespostaEnviada    NVARCHAR(MAX)  NULL,   -- o que o atendente realmente mandou
        DataHoraEnvio      DATETIME       NULL,
        Similaridade       DECIMAL(5,2)   NULL,   -- 0 a 100 (100 = mandou igual)

        Feedback           SMALLINT       NULL,   -- 1 = bom, -1 = ruim
        DataHoraFeedback   DATETIME       NULL,

        NotaAvaliacao      SMALLINT       NULL,   -- avaliação automática: 2 certa, 1 parcial, 0 errada
        MotivoAvaliacao    NVARCHAR(500)  NULL
    );

    CREATE INDEX IX_AiSugestaoLog_DataHora ON dbo.AiSugestaoLog (Origem, DataHora) INCLUDE (CodEmp, Confianca, Similaridade, Feedback, DataHoraEnvio);
    CREATE INDEX IX_AiSugestaoLog_Ticket   ON dbo.AiSugestaoLog (CodTicketChamadoC);
END
GO

-- Consultas úteis
-- Aproveitamento dos últimos 30 dias (mesma conta do /api/ia/relatorio):
-- SELECT Confianca, COUNT(*) Geradas, SUM(CASE WHEN DataHoraEnvio IS NOT NULL THEN 1 ELSE 0 END) Enviadas,
--        AVG(CAST(Similaridade AS FLOAT)) SimilaridadeMedia
--   FROM AiSugestaoLog WHERE Origem = 'ticket' AND DataHora >= DATEADD(DAY,-30,GETDATE()) GROUP BY Confianca;
--
-- Sugestões que o atendente reescreveu (para achar o que falta na documentação):
-- SELECT TOP 50 CodTicketChamadoC, Pergunta, RespostaIA, RespostaEnviada, Similaridade, Fontes
--   FROM AiSugestaoLog WHERE Origem = 'ticket' AND Similaridade < 40 ORDER BY DataHora DESC;
