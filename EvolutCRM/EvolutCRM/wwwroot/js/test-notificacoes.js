// ========================================
// 🧪 TESTE DE NOTIFICAÇÕES - SCRIPT
// ========================================
// Copiar e colar no Console (F12) para testar

// 📅 Teste 1: Enviar notificação simples
window.agendasNotificacoes.enviarNotificacao(
    "📅 Teste de Notificação",
    "Esta é uma notificação de teste!",
    "criacao"
);

// 📅 Teste 2: Verificar Service Worker
console.log('Service Worker status:');
navigator.serviceWorker.getRegistrations().then(regs => {
    console.log('Registrados:', regs.length);
    regs.forEach(reg => console.log(' -', reg.scope));
});

// 📅 Teste 3: Limpar localStorage
console.log('Limpando localStorage...');
Object.keys(localStorage).forEach(key => {
    if (key.startsWith('agenda_notificacao_')) {
        localStorage.removeItem(key);
        console.log('Removido:', key);
    }
});

// 📅 Teste 4: Simular agenda próxima
console.log('Simulando agenda em 30 minutos...');
window.agendasNotificacoes.marcarNotificacaoEnviada(999, '30min');
console.log('Marcado:', window.agendasNotificacoes.jaFoiEnviada(999, '30min'));

// 📅 Teste 5: Verificar permissão de notificação
console.log('Permissão:', Notification.permission);

// 📅 Teste 6: Abrir DevTools Service Worker
console.log('%c✅ Service Worker Details:', 'color: green; font-size: 14px; font-weight: bold;');
console.log('URL:', navigator.serviceWorker.controller?.scriptURL || 'Não registrado');
console.log('Estado:', navigator.serviceWorker.controller?.state || 'Inativo');
