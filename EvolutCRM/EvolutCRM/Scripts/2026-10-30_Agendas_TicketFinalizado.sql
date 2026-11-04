-- 2026-10-30 - Agendas de ticket finalizado
-- A partir desta versão, finalizar o ticket NÃO resolve mais a agenda, e a agenda pendente de
-- ticket finalizado volta a aparecer na tela de Agendas e nos avisos.
-- Antes disso, ticket finalizado pelo botão "Salvar ticket" deixava a agenda como pendente, mas escondida.
-- Rode ANTES de publicar para essas agendas antigas não reaparecerem todas de uma vez como vencidas.

-- 1) Conferir quantas são
SELECT D.CodEmp, COUNT(1) AS Qtd, MIN(D.DataHoraAgenda) AS MaisAntiga, MAX(D.DataHoraAgenda) AS MaisNova
FROM TicketChamadoD D
INNER JOIN TicketChamadoC C ON C.Codigo = D.CodTicketChamadoC AND C.CodEmp = D.CodEmp
WHERE ISNULL(D.NovaAgenda, 'N') = 'S'
  AND ISNULL(D.AgendaResolvida, 'N') = 'N'
  AND ISNULL(C.CodSituacao, 1) = 3
GROUP BY D.CodEmp;

-- 2) Marcar como resolvidas (era como já se comportavam: escondidas)
UPDATE D
SET D.AgendaResolvida = 'S', D.NotificarAgenda = 'N'
FROM TicketChamadoD D
INNER JOIN TicketChamadoC C ON C.Codigo = D.CodTicketChamadoC AND C.CodEmp = D.CodEmp
WHERE ISNULL(D.NovaAgenda, 'N') = 'S'
  AND ISNULL(D.AgendaResolvida, 'N') = 'N'
  AND ISNULL(C.CodSituacao, 1) = 3;
