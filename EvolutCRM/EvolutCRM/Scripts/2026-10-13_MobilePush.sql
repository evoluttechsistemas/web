/* =====================================================================
   HELP Mobile — inscrições de notificação (Web Push) dos celulares
   Rodar UMA vez no banco antes de publicar a versão nova.
   Pode rodar de novo sem problema (só cria o que ainda não existe).
   ===================================================================== */

IF OBJECT_ID('dbo.MobilePushInscricao', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MobilePushInscricao
    (
        Codigo               INT            IDENTITY(1,1) NOT NULL,
        CodEmp               INT            NOT NULL,
        Usuario              VARCHAR(80)    NOT NULL,
        Endpoint             VARCHAR(700)   NOT NULL,   -- endereço de push do celular
        P256dh               VARCHAR(200)   NOT NULL,
        Auth                 VARCHAR(100)   NOT NULL,
        Dispositivo          NVARCHAR(200)  NULL,
        DataHoraCadastro     DATETIME       NOT NULL CONSTRAINT DF_MobilePushInscricao_Cadastro DEFAULT (GETDATE()),
        DataHoraUltimoEnvio  DATETIME       NULL,
        Falhas               INT            NOT NULL CONSTRAINT DF_MobilePushInscricao_Falhas DEFAULT (0),
        CONSTRAINT PK_MobilePushInscricao PRIMARY KEY (Codigo)
    );

    CREATE UNIQUE INDEX UX_MobilePushInscricao_Endpoint ON dbo.MobilePushInscricao (Endpoint);
    CREATE INDEX IX_MobilePushInscricao_Usuario ON dbo.MobilePushInscricao (CodEmp, Usuario);
END
GO
