-- Execute no banco configurado como Connection no HELP, antes de publicar os arquivos.
-- Apenas acrescenta colunas. Não emite boletos nem altera os registros existentes.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.ContaReceber', N'U') IS NULL
    THROW 50000, 'Tabela dbo.ContaReceber não encontrada.', 1;

IF COL_LENGTH('dbo.ContaReceber', 'BoletoPdf') IS NULL
    ALTER TABLE dbo.ContaReceber ADD BoletoPdf varbinary(max) NULL;
IF COL_LENGTH('dbo.ContaReceber', 'BoletoPdfNome') IS NULL
    ALTER TABLE dbo.ContaReceber ADD BoletoPdfNome nvarchar(180) NULL;
IF COL_LENGTH('dbo.ContaReceber', 'BoletoPdfSalvoEm') IS NULL
    ALTER TABLE dbo.ContaReceber ADD BoletoPdfSalvoEm datetime2(0) NULL;
IF COL_LENGTH('dbo.ContaReceber', 'BoletoInterCodigoSolicitacao') IS NULL
    ALTER TABLE dbo.ContaReceber ADD BoletoInterCodigoSolicitacao varchar(100) NULL;
IF COL_LENGTH('dbo.ContaReceber', 'BoletoInterEstado') IS NULL
    ALTER TABLE dbo.ContaReceber ADD BoletoInterEstado varchar(30) NULL;
IF COL_LENGTH('dbo.ContaReceber', 'BoletoInterEmitidoEm') IS NULL
    ALTER TABLE dbo.ContaReceber ADD BoletoInterEmitidoEm datetime2(0) NULL;
IF COL_LENGTH('dbo.ContaReceber', 'BoletoInterUsuario') IS NULL
    ALTER TABLE dbo.ContaReceber ADD BoletoInterUsuario nvarchar(100) NULL;
IF COL_LENGTH('dbo.ContaReceber', 'BoletoInterLinhaDigitavel') IS NULL
    ALTER TABLE dbo.ContaReceber ADD BoletoInterLinhaDigitavel varchar(100) NULL;
IF COL_LENGTH('dbo.ContaReceber', 'BoletoInterSituacaoBanco') IS NULL
    ALTER TABLE dbo.ContaReceber ADD BoletoInterSituacaoBanco varchar(60) NULL;
IF COL_LENGTH('dbo.ContaReceber', 'BoletoInterConsultadoEm') IS NULL
    ALTER TABLE dbo.ContaReceber ADD BoletoInterConsultadoEm datetime2(0) NULL;
IF COL_LENGTH('dbo.ContaReceber', 'BoletoInterJsonEnvio') IS NULL
    ALTER TABLE dbo.ContaReceber ADD BoletoInterJsonEnvio nvarchar(max) NULL;
IF COL_LENGTH('dbo.ContaReceber', 'BoletoInterErro') IS NULL
    ALTER TABLE dbo.ContaReceber ADD BoletoInterErro nvarchar(500) NULL;
COMMIT;

-- Datas novas em UTC. O PDF é binário, sem Base64 e sem caminho físico.
-- NossoNumero continua recebendo codigoSolicitacao, como na integração Forms enviada.
-- O identificador também fica na nova coluna explícita para não confundi-lo com
-- o nosso número bancário retornado na consulta do Inter.
