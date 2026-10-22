-- =============================================================
-- Reuniões do HELP — versão 2 (convites com notificação, recusar, tocar de novo)
-- Rode DEPOIS do 2026-10-17_Reunioes.sql. Pode rodar mais de uma vez.
-- =============================================================

-- Resposta ao convite: NULL = ainda não respondeu | 'Aceito' | 'Recusado'
IF COL_LENGTH('dbo.ReuniaoHelpParticipante', 'Resposta') IS NULL
    ALTER TABLE ReuniaoHelpParticipante ADD Resposta VARCHAR(20) NULL;
GO

-- Quem convidou (para mostrar "Fulano convidou você")
IF COL_LENGTH('dbo.ReuniaoHelpParticipante', 'ConvidadoPor') IS NULL
    ALTER TABLE ReuniaoHelpParticipante ADD ConvidadoPor VARCHAR(100) NULL;
GO

-- Quando o convite foi enviado (o alerta de chamada só toca para convites recentes)
IF COL_LENGTH('dbo.ReuniaoHelpParticipante', 'DataConvite') IS NULL
    ALTER TABLE ReuniaoHelpParticipante ADD DataConvite DATETIME NULL;
GO

-- Convites já existentes: usa a data de criação da reunião
UPDATE p
SET DataConvite = r.DataCriacao,
    ConvidadoPor = ISNULL(p.ConvidadoPor, r.CriadoPor)
FROM ReuniaoHelpParticipante p
INNER JOIN ReuniaoHelp r ON r.Codigo = p.CodReuniao
WHERE p.DataConvite IS NULL;
GO

-- Antes a presença sobrescrevia o papel; volta o papel correto
UPDATE p
SET Situacao = CASE WHEN UPPER(LTRIM(RTRIM(p.Usuario))) = UPPER(LTRIM(RTRIM(r.CriadoPor))) THEN 'Organizador' ELSE 'Convidado' END
FROM ReuniaoHelpParticipante p
INNER JOIN ReuniaoHelp r ON r.Codigo = p.CodReuniao
WHERE p.Situacao NOT IN ('Organizador', 'Convidado');
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ReuniaoHelpParticipante_Convite')
    CREATE INDEX IX_ReuniaoHelpParticipante_Convite
        ON ReuniaoHelpParticipante (Usuario, Resposta, DataConvite);
GO
