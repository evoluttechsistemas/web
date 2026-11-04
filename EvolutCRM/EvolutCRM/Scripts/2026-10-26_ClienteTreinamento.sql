-- =====================================================================
-- Histórico de treinamento do cliente (implantação + orientações)
-- Rodar ANTES de publicar. Pode rodar mais de uma vez.
-- =====================================================================

-- Cada item que o cliente aprendeu (ou que ainda falta no treinamento inicial)
IF OBJECT_ID('dbo.ClienteTreinamento', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ClienteTreinamento
    (
        Codigo             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ClienteTreinamento PRIMARY KEY,
        CodEmp             INT            NOT NULL,
        CodCliente         INT            NOT NULL,
        Titulo             NVARCHAR(200)  NOT NULL,          -- "Entrada de mercadoria por XML"
        Modulo             VARCHAR(30)    NOT NULL CONSTRAINT DF_ClienteTreinamento_Modulo DEFAULT ('outro'),
        Descricao          NVARCHAR(1000) NULL,              -- o que foi explicado (resumo)
        Status             VARCHAR(10)    NOT NULL CONSTRAINT DF_ClienteTreinamento_Status DEFAULT ('treinado'), -- treinado | pendente
        Inicial            BIT            NOT NULL CONSTRAINT DF_ClienteTreinamento_Inicial DEFAULT (0),          -- item do treinamento inicial
        Origem             VARCHAR(12)    NOT NULL CONSTRAINT DF_ClienteTreinamento_Origem DEFAULT ('manual'),    -- inicial | ticket | ligacao | remoto | presencial | manual
        CodTicketChamadoC  INT            NULL,              -- ticket em que foi ensinado (último)
        DataTreinamento    DATETIME       NULL,              -- primeira vez
        UltimaData         DATETIME       NULL,              -- última vez que foi orientado
        Vezes              INT            NOT NULL CONSTRAINT DF_ClienteTreinamento_Vezes DEFAULT (0),
        Atendente          NVARCHAR(100)  NULL,
        PessoaTreinada     NVARCHAR(100)  NULL,              -- quem foi treinado no cliente
        Observacao         NVARCHAR(500)  NULL,
        CodDoc             INT            NULL,              -- tutorial ligado (DocumentacaoTutorial)
        CodEmpDoc          INT            NULL,
        SlugDoc            NVARCHAR(200)  NULL,
        TituloDoc          NVARCHAR(200)  NULL,
        IncluidoPorIA      BIT            NOT NULL CONSTRAINT DF_ClienteTreinamento_IA DEFAULT (0),
        Excluido           CHAR(1)        NOT NULL CONSTRAINT DF_ClienteTreinamento_Exc DEFAULT ('N'),
        DataCriacao        DATETIME       NOT NULL CONSTRAINT DF_ClienteTreinamento_Data DEFAULT (GETDATE()),
        UsuarioCriacao     NVARCHAR(100)  NULL,
        DataAlteracao      DATETIME       NULL,
        UsuarioAlteracao   NVARCHAR(100)  NULL
    );

    CREATE INDEX IX_ClienteTreinamento_Cliente ON dbo.ClienteTreinamento (CodEmp, CodCliente, Excluido) INCLUDE (Titulo, Status, Inicial, UltimaData);
END
GO

-- Tickets já analisados (para não registrar de novo quando o ticket é salvo depois de finalizado)
IF OBJECT_ID('dbo.ClienteTreinamentoTicket', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ClienteTreinamentoTicket
    (
        CodTicketChamadoC  INT       NOT NULL CONSTRAINT PK_ClienteTreinamentoTicket PRIMARY KEY,
        UltimoCodigoD      INT       NOT NULL,   -- última mensagem lida na análise
        QtdItens           INT       NOT NULL,
        DataProcessamento  DATETIME  NOT NULL CONSTRAINT DF_ClienteTreinamentoTicket_Data DEFAULT (GETDATE())
    );
END
GO

-- Consultas úteis
-- O que mais se ensina (bom para priorizar documentação e o treinamento inicial):
-- SELECT Titulo, COUNT(DISTINCT CodCliente) Clientes, SUM(Vezes) Vezes
--   FROM ClienteTreinamento WHERE Excluido = 'N' AND Status = 'treinado' AND Inicial = 0
--  GROUP BY Titulo ORDER BY Vezes DESC;
