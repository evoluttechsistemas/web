-- =====================================================================
-- Cadastro dos treinamentos que a EvolutTech faz (lista do "Treinamento inicial")
-- Rodar DEPOIS do 2026-10-26_ClienteTreinamento.sql. Pode rodar mais de uma vez.
-- =====================================================================

IF OBJECT_ID('dbo.TreinamentoTipo', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TreinamentoTipo
    (
        Codigo            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TreinamentoTipo PRIMARY KEY,
        CodEmp            INT            NOT NULL,
        Titulo            NVARCHAR(200)  NOT NULL,
        Modulo            VARCHAR(30)    NOT NULL CONSTRAINT DF_TreinamentoTipo_Modulo DEFAULT ('outro'),
        Ordem             INT            NOT NULL CONSTRAINT DF_TreinamentoTipo_Ordem DEFAULT (0),
        Ativo             BIT            NOT NULL CONSTRAINT DF_TreinamentoTipo_Ativo DEFAULT (1),
        CodDoc            INT            NULL,      -- tutorial padrão do item
        CodEmpDoc         INT            NULL,
        SlugDoc           NVARCHAR(200)  NULL,
        TituloDoc         NVARCHAR(200)  NULL,
        DataGravacao      DATETIME       NOT NULL CONSTRAINT DF_TreinamentoTipo_Data DEFAULT (GETDATE()),
        UsuarioGravacao   NVARCHAR(100)  NULL
    );

    CREATE INDEX IX_TreinamentoTipo_Emp ON dbo.TreinamentoTipo (CodEmp, Ativo, Ordem);
END
GO

-- Liga o item do cliente ao tipo de treinamento (para atualizar os clientes quando a lista mudar)
IF COL_LENGTH('dbo.ClienteTreinamento', 'CodTreinamentoTipo') IS NULL
    ALTER TABLE dbo.ClienteTreinamento ADD CodTreinamentoTipo INT NULL;
GO

-- Marca as empresas que já receberam a lista padrão (excluir todos os itens não recria a lista).
-- O sistema também cria esta tabela sozinho no primeiro acesso, se ela não existir.
IF OBJECT_ID('dbo.TreinamentoTipoListaPadrao', 'U') IS NULL
    CREATE TABLE dbo.TreinamentoTipoListaPadrao
    (
        CodEmp      INT      NOT NULL CONSTRAINT PK_TreinamentoTipoListaPadrao PRIMARY KEY,
        DataCriacao DATETIME NOT NULL CONSTRAINT DF_TreinamentoTipoListaPadrao_Data DEFAULT (GETDATE())
    );
GO

INSERT INTO dbo.TreinamentoTipoListaPadrao (CodEmp)
SELECT DISTINCT T.CodEmp FROM dbo.TreinamentoTipo T
 WHERE NOT EXISTS (SELECT 1 FROM dbo.TreinamentoTipoListaPadrao M WHERE M.CodEmp = T.CodEmp);
GO
