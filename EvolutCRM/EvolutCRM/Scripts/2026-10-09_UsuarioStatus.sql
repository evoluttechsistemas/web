/* =====================================================================
   HELP — Status do usuário (Banheiro, Treinamento, Assunto pessoal...)
   Rodar UMA vez no banco antes de publicar a versão nova.
   Pode rodar de novo sem problema (só cria o que ainda não existe).
   ===================================================================== */

IF OBJECT_ID('dbo.UsuarioStatus', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.UsuarioStatus
    (
        Codigo                  INT          NOT NULL,
        CodEmp                  INT          NOT NULL,
        Descricao               VARCHAR(60)  NOT NULL,
        Cor                     VARCHAR(7)   NOT NULL CONSTRAINT DF_UsuarioStatus_Cor          DEFAULT ('#f59e0b'),
        Disponivel              CHAR(1)      NOT NULL CONSTRAINT DF_UsuarioStatus_Disponivel   DEFAULT ('N'),
        RedistribuiTickets      CHAR(1)      NOT NULL CONSTRAINT DF_UsuarioStatus_Redistribui  DEFAULT ('N'),
        Ordem                   INT          NOT NULL CONSTRAINT DF_UsuarioStatus_Ordem        DEFAULT (0),
        Ativo                   CHAR(1)      NOT NULL CONSTRAINT DF_UsuarioStatus_Ativo        DEFAULT ('S'),
        DataHoraUltimaGravacao  DATETIME     NULL,
        UsuarioUltimaGravacao   VARCHAR(80)  NULL,
        CONSTRAINT PK_UsuarioStatus PRIMARY KEY (CodEmp, Codigo)
    );
END
GO

-- Status atual de cada usuário (NULL = Disponível)
IF COL_LENGTH('dbo.Usuario', 'CodUsuarioStatus') IS NULL
    ALTER TABLE dbo.Usuario ADD CodUsuarioStatus INT NULL;
GO

-- Desde quando o usuário está no status atual
IF COL_LENGTH('dbo.Usuario', 'DataHoraStatus') IS NULL
    ALTER TABLE dbo.Usuario ADD DataHoraStatus DATETIME NULL;
GO

/* Os status padrão (Banheiro, Assunto pessoal, Treinamento) são criados
   automaticamente pelo sistema para cada empresa que ainda não tiver nenhum. */
