// ========================================
// 📅 SERVICE WORKER - NOTIFICAÇÕES AGENDAS
// ========================================
// Este arquivo roda em background mesmo quando a aba está fechada/minimizada

const CACHE_NAME = 'agenda-cache-v1';
const ASSETS_TO_CACHE = [
    // Apenas assets estáticos que existem
    '/app.css',
    '/js/crm-notify.js',
    // '/js/agenda-notificacoes.js',
    '/images/favicon.ico'
    // ❌ Não cachear:
    // - /index.html (gerado dinamicamente)
    // - /EvolutCRM.styles.css (pode mudar)
    // - /_framework/* (JavaScript framework)
];

// 🔧 INSTALAÇÃO DO SERVICE WORKER
self.addEventListener('install', event => {
    console.log('📦 Service Worker instalado');
    event.waitUntil(
        caches.open(CACHE_NAME).then(cache => {
            console.log('💾 Cache criado para:', ASSETS_TO_CACHE.length, 'assets');
            
            // Cachear assets um por um, ignorar falhas individuais
            return Promise.all(
                ASSETS_TO_CACHE.map(url => {
                    return cache.add(url).catch(err => {
                        console.warn(`⚠️ Não foi possível cachear ${url}:`, err.message);
                        // Continuar mesmo se um asset falhar
                        return Promise.resolve();
                    });
                })
            ).then(() => {
                console.log('✅ Cache setup completo');
                return Promise.resolve();
            });
        })
        .catch(err => {
            console.error('❌ Erro fatal ao criar cache:', err);
            return Promise.resolve(); // Continuar mesmo assim
        })
    );
    self.skipWaiting(); // Ativar imediatamente
});

// 🔄 ATIVAÇÃO DO SERVICE WORKER
self.addEventListener('activate', event => {
    console.log('✅ Service Worker ativado');
    event.waitUntil(clients.claim());
});

// 🌐 INTERCEPTAR REQUISIÇÕES (offline-first, mas excluir APIs dinâmicas)
self.addEventListener('fetch', event => {
    const url = new URL(event.request.url);
    const pathname = url.pathname;
    
    // ❌ NUNCA cachear estas requisições:
    const isBlazorUrl = pathname.includes('/_blazor') || 
                       pathname.includes('/_framework') ||
                       pathname.includes('/_vs') || // Visual Studio Live Share
                       pathname.includes('/browserLink'); // Browser Link debug
    const isDynamicApi = pathname.includes('/api/') ||
                        pathname.includes('.json') ||
                        pathname.includes('.wasm') ||
                        pathname.includes('.dll');
    const isAuth = pathname.includes('/auth') ||
                  pathname.includes('/login') ||
                  pathname.includes('Identity');
    const isHtml = pathname.endsWith('.html') || pathname === '/';

    // ❌ Não cachear:
    // - Requisições não-GET
    // - URLs Blazor (_blazor, _framework)
    // - APIs dinâmicas
    // - Rotas de autenticação
    // - HTML (pode mudar)
    if (event.request.method !== 'GET' || isBlazorUrl || isDynamicApi || isAuth || isHtml) {
        return; // Deixar pass-through sem cache
    }

    event.respondWith(
        caches.match(event.request).then(response => {
            // Retornar do cache se disponível
            if (response) {
                return response;
            }

            // Caso contrário, fazer requisição de rede
            return fetch(event.request)
                .then(networkResponse => {
                    // ✅ Validar ANTES de cachear
                    if (networkResponse && 
                        networkResponse.status === 200 && 
                        networkResponse.type !== 'error' &&
                        networkResponse.type !== 'opaque') {
                        
                        try {
                            const responseToCache = networkResponse.clone();
                            caches.open(CACHE_NAME).then(cache => {
                                try {
                                    cache.put(event.request, responseToCache)
                                        .catch(err => {
                                            console.warn(`⚠️ Erro ao cachear ${pathname}:`, err.message);
                                        });
                                } catch (err) {
                                    console.warn(`⚠️ Erro ao clonar resposta para ${pathname}:`, err.message);
                                }
                            });
                        } catch (err) {
                            console.warn(`⚠️ Erro ao preparar cache para ${pathname}:`, err.message);
                        }
                    }
                    return networkResponse;
                })
                .catch(err => {
                    // Offline - tentar retornar do cache
                    console.warn(`📡 Erro de rede para ${pathname}:`, err.message);
                    return caches.match(event.request)
                        .then(cachedResponse => {
                            if (cachedResponse) {
                                return cachedResponse;
                            }
                            // Se nem cache temos, retornar erro 503 válido
                            return new Response('Offline - recurso não disponível', {
                                status: 503,
                                statusText: 'Service Unavailable',
                                headers: {
                                    'Content-Type': 'text/plain'
                                }
                            });
                        })
                        .catch(cacheErr => {
                            console.error(`❌ Erro crítico ao acessar cache para ${pathname}:`, cacheErr.message);
                            return new Response('Erro ao processar requisição', {
                                status: 500,
                                statusText: 'Internal Server Error',
                                headers: {
                                    'Content-Type': 'text/plain'
                                }
                            });
                        });
                });
        })
        .catch(err => {
            console.error(`❌ Erro no fetch handler para ${pathname}:`, err.message);
            return new Response('Erro ao processar requisição', {
                status: 500,
                statusText: 'Internal Server Error',
                headers: {
                    'Content-Type': 'text/plain'
                }
            });
        })
    );
});

// 📲 EVENTOS DE NOTIFICAÇÃO
self.addEventListener('notificationclick', event => {
    console.log('🖱️ Notificação clicada:', event.notification.tag);
    event.notification.close();

    // Abrir/focar janela do cliente
    event.waitUntil(
        clients.matchAll({ type: 'window', includeUncontrolled: true }).then(clientList => {
            // Se existe janela aberta, focar nela
            for (let i = 0; i < clientList.length; i++) {
                const client = clientList[i];
                if (client.url === '/' && 'focus' in client) {
                    return client.focus();
                }
            }
            // Caso contrário, abrir nova janela
            if (clients.openWindow) {
                return clients.openWindow('/');
            }
        })
    );
});

self.addEventListener('notificationclose', event => {
    console.log('❌ Notificação fechada:', event.notification.tag);
});

// 🔔 BACKGROUND SYNC (para quando voltar online)
self.addEventListener('sync', event => {
    console.log('🔄 Background sync:', event.tag);
    if (event.tag === 'sync-agendas') {
        event.waitUntil(syncAgendas());
    }
});

// 📡 SINCRONIZAR AGENDAS EM BACKGROUND
async function syncAgendas() {
    try {
        console.log('📡 Sincronizando agendas...');
        // Aqui você pode fazer requisição para sincronizar agendas
        const response = await fetch('/api/agendas/proximas');
        if (response.ok) {
            console.log('✅ Agendas sincronizadas');
        }
    } catch (err) {
        console.error('❌ Erro ao sincronizar:', err);
    }
}

// 📣 RECEBER MENSAGENS DO CLIENTE
self.addEventListener('message', event => {
    console.log('📨 Mensagem recebida no SW:', event.data);

    if (event.data && event.data.type === 'SKIP_WAITING') {
        self.skipWaiting();
    }

    if (event.data && event.data.type === 'SHOW_NOTIFICATION') {
        const { titulo, corpo, tipo } = event.data;
        self.registration.showNotification(titulo, {
            body: corpo,
            icon: '/images/favicon.ico',
            badge: '/images/favicon.ico',
            tag: `agenda-${tipo}-${Date.now()}`,
            requireInteraction: tipo !== 'criacao',
            vibrate: [200, 100, 200]
        });
    }
});

console.log('✅ Service Worker carregado');
