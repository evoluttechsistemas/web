-- =============================================================
-- Reuniões do HELP (vídeo, áudio, chat e compartilhamento de tela)
-- Pode rodar mais de uma vez: só cria o que ainda não existe.
-- =============================================================

IF OBJECT_ID('dbo.ReuniaoHelp', 'U') IS NULL
BEGIN
    CREATE TABLE ReuniaoHelp
    (
        Codigo              INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CodEmp              INT NOT NULL,
        Titulo              VARCHAR(150) NOT NULL,
        CriadoPor           VARCHAR(100) NOT NULL,
        DataCriacao         DATETIME NOT NULL CONSTRAINT DF_ReuniaoHelp_DataCriacao DEFAULT GETDATE(),
        DataEncerramento    DATETIME NULL,
        Status              VARCHAR(20) NOT NULL CONSTRAINT DF_ReuniaoHelp_Status DEFAULT 'Aberta',  -- Aberta | Encerrada
        LimiteParticipantes INT NOT NULL CONSTRAINT DF_ReuniaoHelp_Limite DEFAULT 6
    );

    CREATE INDEX IX_ReuniaoHelp_CodEmp_Status
        ON ReuniaoHelp (CodEmp, Status, DataCriacao DESC);
END
GO

IF OBJECT_ID('dbo.ReuniaoHelpParticipante', 'U') IS NULL
BEGIN
    CREATE TABLE ReuniaoHelpParticipante
    (
        Codigo      INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CodReuniao  INT NOT NULL,
        Usuario     VARCHAR(100) NOT NULL,
        Situacao    VARCHAR(20) NOT NULL CONSTRAINT DF_ReuniaoHelpParticipante_Situacao DEFAULT 'Convidado', -- papel: Organizador | Convidado
        DataEntrada DATETIME NULL,
        DataSaida   DATETIME NULL,
        CONSTRAINT FK_ReuniaoHelpParticipante_Reuniao
            FOREIGN KEY (CodReuniao) REFERENCES ReuniaoHelp(Codigo),
        CONSTRAINT UQ_ReuniaoHelpParticipante UNIQUE (CodReuniao, Usuario)
    );

    CREATE INDEX IX_ReuniaoHelpParticipante_Usuario
        ON ReuniaoHelpParticipante (Usuario, CodReuniao);
END
GO

IF OBJECT_ID('dbo.ReuniaoHelpMensagem', 'U') IS NULL
BEGIN
    CREATE TABLE ReuniaoHelpMensagem
    (
        Codigo      INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CodReuniao  INT NOT NULL,
        Usuario     VARCHAR(100) NOT NULL,
        Mensagem    VARCHAR(2000) NOT NULL,
        DataHora    DATETIME NOT NULL CONSTRAINT DF_ReuniaoHelpMensagem_DataHora DEFAULT GETDATE(),
        CONSTRAINT FK_ReuniaoHelpMensagem_Reuniao
            FOREIGN KEY (CodReuniao) REFERENCES ReuniaoHelp(Codigo)
    );

    CREATE INDEX IX_ReuniaoHelpMensagem_Reuniao
        ON ReuniaoHelpMensagem (CodReuniao, DataHora);
END
GO

-- Libera a permissão "Acessar reuniões" para todos os usuários ativos do HELP.
-- (Depois dá para tirar de alguém pela tela Admin > Permissões, módulo "Reuniões".)
INSERT INTO UsuarioPermissaoCRM
    (CodUsuario, Modulo, Permissao, Ativo, UsuarioUltimaGravacao, DataHoraUltimaGravacao, CodEmp)
SELECT u.Codigo, 'Reuniões', 'REUNIOES_ACESSAR', 'S', 'EVOLUT', GETDATE(), u.CodEmp
FROM Usuario u
WHERE ISNULL(u.Inativo, 'N') <> 'S'
  AND ISNULL(u.Help, 'N') = 'S'
  AND NOT EXISTS
  (
      SELECT 1
      FROM UsuarioPermissaoCRM p
      WHERE p.CodUsuario = u.Codigo
        AND p.CodEmp = u.CodEmp
        AND p.Permissao = 'REUNIOES_ACESSAR'
  );
GO
