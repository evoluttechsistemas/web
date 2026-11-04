-- =====================================================================
-- IA - Assistente do atendente (chat com a IA dentro do ticket)
-- Rodar ANTES de publicar. Pode rodar mais de uma vez.
-- (Sem as tabelas o assistente funciona, só não guarda o histórico.)
-- =====================================================================

IF OBJECT_ID('dbo.TicketAssistenteIA', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TicketAssistenteIA
    (
        CodTicketAssistenteIA BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TicketAssistenteIA PRIMARY KEY,
        CodTicketChamadoC     INT            NOT NULL,
        CodEmp                INT            NULL,
        Usuario               NVARCHAR(100)  NULL,            -- quem perguntou (ou quem pediu, na resposta da IA)
        Papel                 VARCHAR(12)    NOT NULL,        -- atendente | ia
        Mensagem              NVARCHAR(MAX)  NULL,
        Fontes                NVARCHAR(MAX)  NULL,            -- JSON: documentações, casos e links da web usados
        PesquisouWeb          BIT            NOT NULL CONSTRAINT DF_TicketAssistenteIA_Web DEFAULT (0),
        Modelo                VARCHAR(60)    NULL,
        DuracaoMs             INT            NULL,
        TokensEntrada         INT            NULL,
        TokensSaida           INT            NULL,
        Arquivado             CHAR(1)        NOT NULL CONSTRAINT DF_TicketAssistenteIA_Arq DEFAULT ('N'),  -- "Nova conversa"
        DataHora              DATETIME       NOT NULL CONSTRAINT DF_TicketAssistenteIA_DataHora DEFAULT (GETDATE())
    );

    CREATE INDEX IX_TicketAssistenteIA_Ticket ON dbo.TicketAssistenteIA (CodTicketChamadoC, Arquivado, CodTicketAssistenteIA);
    CREATE INDEX IX_TicketAssistenteIA_Data   ON dbo.TicketAssistenteIA (DataHora) INCLUDE (CodEmp, Papel, PesquisouWeb, TokensEntrada, TokensSaida);
END
GO

-- Prints colados no chat do assistente (ou imagens do ticket enviadas com "Analisar com IA")
IF OBJECT_ID('dbo.TicketAssistenteIAAnexo', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TicketAssistenteIAAnexo
    (
        CodAnexo              BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TicketAssistenteIAAnexo PRIMARY KEY,
        CodTicketAssistenteIA BIGINT         NOT NULL,
        Origem                VARCHAR(10)    NOT NULL,        -- colado | ticket
        CodTicketChamadoD     INT            NULL,            -- quando veio de uma mensagem do ticket
        Imagem                VARBINARY(MAX) NULL,            -- JPEG reduzido (até 1600px)
        DataHora              DATETIME       NOT NULL CONSTRAINT DF_TicketAssistenteIAAnexo_DataHora DEFAULT (GETDATE())
    );

    CREATE INDEX IX_TicketAssistenteIAAnexo_Msg ON dbo.TicketAssistenteIAAnexo (CodTicketAssistenteIA);
END
GO

-- Consultas úteis
-- Uso e custo aproximado por dia:
-- SELECT CAST(DataHora AS DATE) Dia, COUNT(*) Respostas, SUM(CAST(PesquisouWeb AS INT)) ComWeb,
--        SUM(TokensEntrada) Entrada, SUM(TokensSaida) Saida, AVG(DuracaoMs) TempoMedioMs
--   FROM TicketAssistenteIA WHERE Papel = 'ia' GROUP BY CAST(DataHora AS DATE) ORDER BY 1 DESC;
--
-- O que os atendentes mais perguntam (para achar o que falta na documentação):
-- SELECT TOP 100 CodTicketChamadoC, Usuario, Mensagem, DataHora
--   FROM TicketAssistenteIA WHERE Papel = 'atendente' ORDER BY DataHora DESC;
