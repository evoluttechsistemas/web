-- =====================================================================
-- IA - Gabarito fixo (perguntas reais + resposta certa) e as execuções
-- O sistema cria estas tabelas sozinho (IaGabaritoService). Rode só se faltar permissão de CREATE TABLE.
-- Pode rodar mais de uma vez.
-- =====================================================================
IF OBJECT_ID('dbo.AiGabarito', 'U') IS NULL
    CREATE TABLE dbo.AiGabarito
    (
        Codigo           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AiGabarito PRIMARY KEY,
        CodEmp           INT            NOT NULL,
        Pergunta         NVARCHAR(2000) NOT NULL,   -- como o cliente escreveu (com erro de digitação e tudo)
        RespostaEsperada NVARCHAR(2000) NOT NULL,   -- o que de fato resolve, conferido pelo time
        Assunto          NVARCHAR(200)  NULL,
        CodTicketOrigem  INT            NULL,       -- a busca ignora os casos deste ticket na prova
        Ativo            BIT            NOT NULL CONSTRAINT DF_AiGabarito_Ativo DEFAULT (1),
        CriadoPor        NVARCHAR(100)  NULL,
        CriadoEm         DATETIME       NOT NULL CONSTRAINT DF_AiGabarito_CriadoEm DEFAULT (GETDATE())
    );
IF OBJECT_ID('dbo.AiGabaritoExecucao', 'U') IS NULL
    CREATE TABLE dbo.AiGabaritoExecucao
    (
        Codigo     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AiGabaritoExecucao PRIMARY KEY,
        CodEmp     INT           NOT NULL,
        DataHora   DATETIME      NOT NULL CONSTRAINT DF_AiGabaritoExecucao_DataHora DEFAULT (GETDATE()),
        Usuario    NVARCHAR(100) NULL,
        Modelo     VARCHAR(120)  NULL,
        Total      INT           NOT NULL CONSTRAINT DF_AiGabaritoExecucao_Total DEFAULT (0),
        Certas     INT           NOT NULL CONSTRAINT DF_AiGabaritoExecucao_Certas DEFAULT (0),
        Parciais   INT           NOT NULL CONSTRAINT DF_AiGabaritoExecucao_Parciais DEFAULT (0),
        Erradas    INT           NOT NULL CONSTRAINT DF_AiGabaritoExecucao_Erradas DEFAULT (0),
        Falhas     INT           NOT NULL CONSTRAINT DF_AiGabaritoExecucao_Falhas DEFAULT (0),
        Concluida  BIT           NOT NULL CONSTRAINT DF_AiGabaritoExecucao_Concluida DEFAULT (0)
    );
IF OBJECT_ID('dbo.AiGabaritoResultado', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AiGabaritoResultado
    (
        Codigo       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AiGabaritoResultado PRIMARY KEY,
        CodExecucao  INT           NOT NULL,
        CodGabarito  INT           NOT NULL,
        Nota         SMALLINT      NOT NULL,      -- 2 certa, 1 parcial, 0 errada, -1 falhou
        Motivo       NVARCHAR(500) NULL,
        Resposta     NVARCHAR(MAX) NULL,
        Confianca    VARCHAR(10)   NULL
    );
    CREATE INDEX IX_AiGabaritoResultado_Exec ON dbo.AiGabaritoResultado (CodExecucao, CodGabarito);
END
GO
