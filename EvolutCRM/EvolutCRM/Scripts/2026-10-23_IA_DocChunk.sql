-- IA do HELP – Passo 4: pedaços de documentação com empresa e sistema
-- Rodar ANTES de publicar a versão nova (o indexador grava nessas colunas).

IF COL_LENGTH('dbo.DocChunk', 'CodEmp') IS NULL
    ALTER TABLE dbo.DocChunk ADD CodEmp INT NULL;

IF COL_LENGTH('dbo.DocChunk', 'Sistema') IS NULL
    ALTER TABLE dbo.DocChunk ADD Sistema NVARCHAR(50) NULL;

-- Pedaços agora têm até ~1.300 caracteres e o título guarda o caminho ("Doc › Passo")
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.DocChunk') AND name = 'Texto' AND max_length <> -1)
    ALTER TABLE dbo.DocChunk ALTER COLUMN Texto NVARCHAR(MAX) NULL;

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.DocChunk') AND name = 'Titulo' AND max_length <> -1 AND max_length < 800)
    ALTER TABLE dbo.DocChunk ALTER COLUMN Titulo NVARCHAR(400) NULL;
