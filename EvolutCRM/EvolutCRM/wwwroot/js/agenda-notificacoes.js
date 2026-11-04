// ========================================
// 📅 SISTEMA DE NOTIFICAÇÕES DE AGENDAS
// ========================================
// Funciona com Service Worker para notificações em background

window.agendasNotificacoes = {
    
    // 🔔 Solicitar permissão de notificação ao usuário
    solicitarPermissao: async function() {
        if ('Notification' in window) {
            if (Notification.permission === 'default') {
                const permission = await Notification.requestPermission();
                console.log('📋 Permissão de notificação:', permission);
                return permission === 'granted';
            }
            return Notification.permission === 'granted';
        }
        return false;
    },

    // 📤 Enviar notificação - via Service Worker (background)
    enviarNotificacao: function(titulo, corpo, tipo = 'padrao') {
        if (!('Notification' in window)) {
            console.warn('⚠️ Notificações não suportadas');
            return;
        }

        if (Notification.permission !== 'granted') {
            console.warn('⚠️ Permissão não concedida');
            return;
        }

        try {
            // 🎯 PREFERÊNCIA: Usar Service Worker se disponível (funciona no background)
            if ('serviceWorker' in navigator && navigator.serviceWorker.controller) {
                console.log(`📨 Enviando via Service Worker: ${tipo}`);
                navigator.serviceWorker.controller.postMessage({
                    type: 'SHOW_NOTIFICATION',
                    titulo: titulo,
                    corpo: corpo,
                    tipo: tipo
                });
            } else {
                // ❌ Fallback: Notificação local (apenas se aba está visível)
                console.log(`📨 Enviando notificação local: ${tipo}`);
                this.enviarNotificacaoLocal(titulo, corpo, tipo);
            }

        } catch (erro) {
            console.error('❌ Erro ao enviar notificação:', erro);
        }
    },

    // 📲 Notificação local (quando aba está visível)
    enviarNotificacaoLocal: function(titulo, corpo, tipo) {
        const opcoes = {
            body: corpo,
            icon: '/images/favicon.ico',
            badge: '/images/favicon.ico',
            tag: `agenda-${tipo}-${Date.now()}`,
            requireInteraction: tipo !== 'criacao',
            vibrate: [200, 100, 200]
        };

        const notificacao = new Notification(titulo, opcoes);
        
        notificacao.onclick = function() {
            window.focus();
            notificacao.close();
        };

        console.log(`✅ Notificação local enviada: ${tipo}`);
    },

    // 🔍 Marcar notificação como enviada
    marcarNotificacaoEnviada: function(codigoAgenda, tipo) {
        const chave = `agenda_notificacao_${codigoAgenda}_${tipo}`;
        localStorage.setItem(chave, '1');
        console.log(`📌 Marcado: ${chave}`);
    },

    // ✅ Verificar se notificação já foi enviada
    jaFoiEnviada: function(codigoAgenda, tipo) {
        const chave = `agenda_notificacao_${codigoAgenda}_${tipo}`;
        const valor = localStorage.getItem(chave);
        return valor === '1' ? '1' : '0';
    },

    // 🗑️ Limpar notificações
    limparNotificacoes: function(codigoAgenda) {
        const tipos = ['criacao', '30min', '5min'];
        tipos.forEach(tipo => {
            const chave = `agenda_notificacao_${codigoAgenda}_${tipo}`;
            localStorage.removeItem(chave);
        });
        console.log(`🗑️ Notificações limpas: ${codigoAgenda}`);
    }
};

// 📋 REGISTRAR SERVICE WORKER
// 📋 REGISTRAR SERVICE WORKER
function registrarServiceWorker() {
    // Service Worker desativado
}

// 🔐 Auto-inicializar ao carregar
document.addEventListener('DOMContentLoaded', function() {
    // Solicitar permissão
    window.agendasNotificacoes.solicitarPermissao();
    
});
