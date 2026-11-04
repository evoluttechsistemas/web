/* =====================================================================
   HELP Mobile — atividade do usuário no celular
   Rodar UMA vez no banco antes de publicar a versão nova.
   Pode rodar de novo sem problema (só cria o que ainda não existe).

   Enquanto o usuário está com o HELP Mobile aberto, o app grava a hora aqui.
   O OfflineTicketReassignService não devolve para a fila (NOVO) os tickets
   de quem teve atividade no celular nos últimos 90 segundos.
   ===================================================================== */

IF COL_LENGTH('dbo.Usuario', 'UltimaAtividadeMobile') IS NULL
    ALTER TABLE dbo.Usuario ADD UltimaAtividadeMobile DATETIME NULL;
GO
