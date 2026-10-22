-- =============================================================
-- Indique e Ganhe — indicações feitas pelo cliente + desconto na mensalidade
-- Pode rodar mais de uma vez: só cria o que ainda não existe.
-- =============================================================

-- Link pessoal de cada cliente (o "outro sistema" usa o Token para montar o botão)
IF OBJECT_ID('dbo.ClienteIndicacaoAcesso', 'U') IS NULL
BEGIN
    CREATE TABLE ClienteIndicacaoAcesso
    (
        Codigo      INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CodEmp      INT NOT NULL,
        CodCliente  INT NOT NULL,
        Token       VARCHAR(64) NOT NULL,
        Ativo       CHAR(1) NOT NULL CONSTRAINT DF_ClienteIndicacaoAcesso_Ativo DEFAULT 'S',
        DataCriacao DATETIME NOT NULL CONSTRAINT DF_ClienteIndicacaoAcesso_Data DEFAULT GETDATE(),
        UltimoAcesso DATETIME NULL,
        CONSTRAINT UQ_ClienteIndicacaoAcesso_Token UNIQUE (Token),
        CONSTRAINT UQ_ClienteIndicacaoAcesso_Cliente UNIQUE (CodEmp, CodCliente)
    );
END
GO

-- Indicações
IF OBJECT_ID('dbo.ClienteIndicacao', 'U') IS NULL
BEGIN
    CREATE TABLE ClienteIndicacao
    (
        Codigo        INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CodEmp        INT NOT NULL,
        CodCliente    INT NOT NULL,                 -- quem indicou
        NomeContato   VARCHAR(150) NOT NULL,
        NomeFantasia  VARCHAR(150) NOT NULL,
        WhatsApp      VARCHAR(20)  NOT NULL,        -- só números, com 55
        Segmento      VARCHAR(80)  NOT NULL,
        Status        VARCHAR(20)  NOT NULL CONSTRAINT DF_ClienteIndicacao_Status DEFAULT 'Recebida',
                      -- Recebida | EmContato | Confirmada (virou cliente = R$ 10 de desconto) | NaoFechou
        Observacao    VARCHAR(500) NULL,            -- anotação interna (não aparece para o cliente)
        Origem        VARCHAR(20)  NOT NULL CONSTRAINT DF_ClienteIndicacao_Origem DEFAULT 'Cliente',
        DataCriacao   DATETIME NOT NULL CONSTRAINT DF_ClienteIndicacao_Data DEFAULT GETDATE(),
        DataStatus    DATETIME NULL,
        UsuarioStatus VARCHAR(100) NULL
    );

    CREATE INDEX IX_ClienteIndicacao_Cliente ON ClienteIndicacao (CodEmp, CodCliente, Status);
    CREATE INDEX IX_ClienteIndicacao_WhatsApp ON ClienteIndicacao (CodEmp, WhatsApp);
END
GO

-- -------------------------------------------------------------
-- Para o OUTRO SISTEMA montar o botão: devolve (e cria se precisar) o link
-- pessoal do cliente, encontrando o cliente pelo CNPJ/CPF (coluna Cliente.Cpf).
--   EXEC sp_IndicacaoLinkCliente @CodEmp = 2, @Cnpj = '12.345.678/0001-90'
-- (também aceita @CodCliente no lugar do CNPJ)
-- -------------------------------------------------------------
IF OBJECT_ID('dbo.sp_IndicacaoLinkCliente', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_IndicacaoLinkCliente;
GO

CREATE PROCEDURE dbo.sp_IndicacaoLinkCliente
    @CodEmp     INT,
    @Cnpj       VARCHAR(20) = NULL,
    @CodCliente INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Doc VARCHAR(20) = REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(@Cnpj, ''), '.', ''), '/', ''), '-', ''), ' ', '');
    DECLARE @Qtd INT;

    IF @CodCliente IS NULL
    BEGIN
        SELECT @Qtd = COUNT(1), @CodCliente = MIN(C.Codigo)
        FROM Cliente C
        WHERE C.CodEmp = @CodEmp
          AND ISNULL(C.Inativo, 'N') <> 'S'
          AND LEN(@Doc) IN (11, 14)
          AND REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(C.Cpf, ''), '.', ''), '/', ''), '-', ''), ' ', '') = @Doc;

        IF ISNULL(@Qtd, 0) = 0
        BEGIN
            SELECT CAST(NULL AS VARCHAR(64)) AS Token, CAST(NULL AS VARCHAR(200)) AS Link,
                   'Nenhum cliente ativo com esse CNPJ/CPF nesta empresa.' AS Erro;
            RETURN;
        END

        IF @Qtd > 1
        BEGIN
            SELECT CAST(NULL AS VARCHAR(64)) AS Token, CAST(NULL AS VARCHAR(200)) AS Link,
                   'Esse CNPJ/CPF está em mais de um cadastro de cliente. Corrija o cadastro duplicado.' AS Erro;
            RETURN;
        END
    END

    IF NOT EXISTS (SELECT 1 FROM ClienteIndicacaoAcesso WHERE CodEmp = @CodEmp AND CodCliente = @CodCliente)
        INSERT INTO ClienteIndicacaoAcesso (CodEmp, CodCliente, Token)
        VALUES (@CodEmp, @CodCliente,
                LOWER(REPLACE(CONVERT(VARCHAR(36), NEWID()), '-', '') + REPLACE(CONVERT(VARCHAR(36), NEWID()), '-', '')));

    SELECT Token,
           'https://help.evoluttech.com/indicacao/' + Token AS Link,
           CAST(NULL AS VARCHAR(200)) AS Erro
    FROM ClienteIndicacaoAcesso
    WHERE CodEmp = @CodEmp AND CodCliente = @CodCliente;
END
GO

-- Permissão da tela interna (libera para quem já gerencia permissões / admin)
INSERT INTO UsuarioPermissaoCRM
    (CodUsuario, Modulo, Permissao, Ativo, UsuarioUltimaGravacao, DataHoraUltimaGravacao, CodEmp)
SELECT p.CodUsuario, 'Indicações', 'INDICACOES_GERENCIAR', 'S', 'EVOLUT', GETDATE(), p.CodEmp
FROM UsuarioPermissaoCRM p
WHERE p.Permissao = 'ADMIN_ACESSAR'
  AND ISNULL(p.Ativo, 'N') = 'S'
  AND NOT EXISTS (SELECT 1 FROM UsuarioPermissaoCRM x
                  WHERE x.CodUsuario = p.CodUsuario AND x.CodEmp = p.CodEmp
                    AND x.Permissao = 'INDICACOES_GERENCIAR');
GO
