// Em desenvolvimento: sempre busca da rede (sem cache offline), mas com as notificações ativas.
self.importScripts('./push-sw.js');

self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));
