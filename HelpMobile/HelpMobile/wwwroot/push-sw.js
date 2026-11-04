// HELP Mobile — notificações (carregado pelo service-worker.js e pelo service-worker.published.js)
// Roda mesmo com o app fechado.

self.addEventListener('push', event => {
    let dados = {};
    try { dados = event.data ? event.data.json() : {}; } catch { dados = { corpo: event.data ? event.data.text() : '' }; }

    const titulo = dados.titulo || 'HELP';
    const url = new URL(dados.url || './', self.registration.scope).href;
    const tag = dados.tag || 'help';

    event.waitUntil((async () => {
        const janelas = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });

        // Avisa o app aberto (atualiza lista/conversa na hora)
        janelas.forEach(c => c.postMessage({ tipo: 'push', ticket: dados.ticket }));

        await self.registration.showNotification(titulo, {
            body: dados.corpo || 'Nova mensagem',
            tag: tag,
            renotify: true,
            icon: 'icon-192.png',
            badge: 'icon-badge.png',
            data: { url: url, ticket: dados.ticket },
            vibrate: [80, 40, 80]
        });

        // Já está com essa conversa aberta na tela: não precisa da notificação (igual ao WhatsApp)
        const naConversa = janelas.some(c =>
            c.visibilityState === 'visible' && dados.ticket && c.url.indexOf('/ticket/' + dados.ticket) >= 0);
        if (naConversa) {
            const abertas = await self.registration.getNotifications({ tag: tag });
            abertas.forEach(n => n.close());
        }
    })());
});

self.addEventListener('notificationclick', event => {
    event.notification.close();
    const url = (event.notification.data && event.notification.data.url) || self.registration.scope;

    event.waitUntil((async () => {
        const janelas = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
        const app = janelas.find(c => c.url.startsWith(self.registration.scope));
        if (app) {
            await app.focus();
            // Navega dentro do app sem recarregar
            app.postMessage({ tipo: 'abrir', url: url });
            return;
        }
        await self.clients.openWindow(url);
    })());
});
