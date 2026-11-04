// HELP Mobile — funções de navegador chamadas pelo Blazor
window.helpMobile = (function () {

    // ── Armazenamento (login fica salvo no aparelho) ─────────────────────────
    function get(chave) { try { return localStorage.getItem(chave); } catch { return null; } }
    function set(chave, valor) { try { localStorage.setItem(chave, valor); } catch { } }
    function del(chave) { try { localStorage.removeItem(chave); } catch { } }

    // ── Página atual: avisada quando o app volta para a frente e quando chega push ──
    let refPagina = null, chavePagina = null;
    function registrarPagina(chave, dotnetRef) { chavePagina = chave; refPagina = dotnetRef; }
    function liberarPagina(chave) { if (chavePagina === chave) { refPagina = null; chavePagina = null; } }

    document.addEventListener('visibilitychange', () => {
        if (refPagina) {
            refPagina.invokeMethodAsync('MudouVisibilidade', document.visibilityState === 'visible')
                .catch(() => { });
        }
    });
    function visivel() { return document.visibilityState === 'visible'; }

    // ── Caixa de mensagem cresce sozinha (sem passar pelo C# a cada letra) ─────
    document.addEventListener('input', (e) => {
        const el = e.target;
        if (el && el.tagName === 'TEXTAREA' && el.hasAttribute('data-auto-altura')) autoAltura(el);
    });

    // ── Maiúscula automática ─────────────────────────────────────────────────
    // Campos com autocapitalize="sentences": o teclado do celular já cuida disso,
    // mas no computador (teclado físico) o navegador ignora. Aqui a letra digitada
    // vira maiúscula quando é a primeira do texto ou começa frase (após . ! ? ou Enter).
    // Roda na fase de captura, antes do Blazor ler o valor do campo.
    window.addEventListener('input', (e) => {
        const el = e.target;
        if (!el || (el.tagName !== 'TEXTAREA' && el.tagName !== 'INPUT')) return;
        if (el.getAttribute('autocapitalize') !== 'sentences') return;
        if (e.isComposing || e.inputType !== 'insertText' || !e.data) return; // só digitação normal
        const pos = el.selectionStart;
        if (pos == null || pos < 1 || el.selectionEnd !== pos) return;
        const letra = el.value.charAt(pos - 1);
        const maiuscula = letra.toLocaleUpperCase('pt-BR');
        if (letra === maiuscula || maiuscula.length !== 1) return;
        const antes = el.value.slice(0, pos - 1);
        if (!/(^|[.!?]\s+|\n)\s*$/.test(antes)) return;
        el.value = antes + maiuscula + el.value.slice(pos);
        el.setSelectionRange(pos, pos);
    }, true);

    // ── Toque na notificação com o app aberto: navega sem recarregar ──────────
    let refNavegacao = null;
    function onNavegar(dotnetRef) { refNavegacao = dotnetRef; }
    if ('serviceWorker' in navigator) {
        navigator.serviceWorker.addEventListener('message', ev => {
            const d = ev.data || {};
            if (d.tipo === 'abrir' && d.url && refNavegacao) {
                refNavegacao.invokeMethodAsync('Navegar', d.url).catch(() => { });
            }
            if (d.tipo === 'push' && refPagina) {
                refPagina.invokeMethodAsync('ChegouPush', d.ticket || 0).catch(() => { });
            }
        });
    }

    // ── Rolagem da conversa ───────────────────────────────────────────────────
    function rolarFim(el, suave) {
        if (!el) return;
        el.scrollTo({ top: el.scrollHeight, behavior: suave ? 'smooth' : 'auto' });
    }
    function pertoDoFim(el) {
        if (!el) return true;
        return el.scrollHeight - el.scrollTop - el.clientHeight < 140;
    }
    function rolarAte(id) {
        const el = document.getElementById(id);
        if (!el) return false;
        el.scrollIntoView({ behavior: 'smooth', block: 'center' });
        el.classList.add('mb-destaque');
        setTimeout(() => el.classList.remove('mb-destaque'), 1400);
        return true;
    }

    // ── Rolagem da conversa observada aqui no navegador ──────────────────────
    // Antes cada evento de scroll chamava o C# e redesenhava a tela inteira (dezenas por segundo).
    // Agora só avisa o Blazor quando muda algo que importa: chegou perto do fim / chegou no topo.
    function observarRolagem(el, ref) {
        if (!el || el._mbRolagem) return;
        let agendado = false, pertoAntes = true, topoAvisado = false;

        const verificar = () => {
            agendado = false;
            const perto = el.scrollHeight - el.scrollTop - el.clientHeight < 140;
            if (perto !== pertoAntes) {
                pertoAntes = perto;
                ref.invokeMethodAsync('RolagemPertoDoFim', perto).catch(() => { });
            }
            if (el.scrollTop < 300) {
                if (!topoAvisado) {
                    topoAvisado = true;
                    ref.invokeMethodAsync('RolagemNoTopo').catch(() => { });
                    setTimeout(() => { topoAvisado = false; }, 800);
                }
            }
        };
        const aoRolar = () => {
            if (agendado) return;
            agendado = true;
            requestAnimationFrame(verificar);
        };
        el.addEventListener('scroll', aoRolar, { passive: true });
        el._mbRolagem = aoRolar;
    }
    function pararRolagem(el) {
        if (el && el._mbRolagem) {
            el.removeEventListener('scroll', el._mbRolagem);
            el._mbRolagem = null;
        }
    }
    // Carregou mensagens antigas em cima: mantém na tela a mesma mensagem que estava sendo lida
    function alturaRolagem(el) { return el ? el.scrollHeight : 0; }
    function manterPosicao(el, alturaAntes) {
        if (!el) return;
        el.scrollTop += el.scrollHeight - alturaAntes;
    }

    // ── Caixa de texto que cresce sozinha ────────────────────────────────────
    function autoAltura(el) {
        if (!el) return;
        el.style.height = 'auto';
        el.style.height = Math.min(el.scrollHeight, 140) + 'px';
    }
    function focar(el) { try { el && el.focus(); } catch { } }
    function limpar(el) { if (el) { el.value = ''; autoAltura(el); } }

    // ── Badge no ícone do app (Android/desktop; iOS instalado) ───────────────
    function badge(n) {
        try {
            if (!('setAppBadge' in navigator)) return;
            if (n > 0) navigator.setAppBadge(n); else navigator.clearAppBadge();
        } catch { }
    }

    async function copiar(texto) {
        try { await navigator.clipboard.writeText(texto); return true; }
        catch {
            try {
                const t = document.createElement('textarea');
                t.value = texto; t.style.position = 'fixed'; t.style.opacity = '0';
                document.body.appendChild(t); t.select();
                const ok = document.execCommand('copy');
                document.body.removeChild(t);
                return ok;
            } catch { return false; }
        }
    }

    function vibrar() { try { navigator.vibrate && navigator.vibrate(60); } catch { } }

    // ── Push ──────────────────────────────────────────────────────────────────
    function base64ParaBytes(base64) {
        const pad = '='.repeat((4 - base64.length % 4) % 4);
        const b64 = (base64 + pad).replace(/-/g, '+').replace(/_/g, '/');
        const raw = atob(b64);
        const out = new Uint8Array(raw.length);
        for (let i = 0; i < raw.length; i++) out[i] = raw.charCodeAt(i);
        return out;
    }

    function ehIOS() {
        return /iphone|ipad|ipod/i.test(navigator.userAgent)
            || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
    }
    function instalado() {
        return window.matchMedia('(display-mode: standalone)').matches || window.navigator.standalone === true;
    }

    /** Retorna: 'ativo' | 'desativado' | 'negado' | 'indisponivel' | 'instalar-ios' | 'inseguro' */
    async function situacaoPush() {
        // Notificação só funciona em HTTPS (ou localhost). Aberto pelo IP em http: só as telas.
        if (!window.isSecureContext) return 'inseguro';
        if (!('serviceWorker' in navigator) || !('PushManager' in window) || !('Notification' in window)) {
            return ehIOS() && !instalado() ? 'instalar-ios' : 'indisponivel';
        }
        if (Notification.permission === 'denied') return 'negado';
        try {
            const reg = await navigator.serviceWorker.ready;
            const sub = await reg.pushManager.getSubscription();
            return sub && Notification.permission === 'granted' ? 'ativo' : 'desativado';
        } catch { return 'desativado'; }
    }

    /** Pede permissão e cria a inscrição. Retorna {endpoint,p256dh,auth} ou {erro}. */
    async function ativarPush(chavePublica) {
        try {
            if (!('serviceWorker' in navigator) || !('PushManager' in window)) {
                return { erro: ehIOS() && !instalado() ? 'instalar-ios' : 'indisponivel' };
            }
            const perm = await Notification.requestPermission();
            if (perm !== 'granted') return { erro: perm === 'denied' ? 'negado' : 'desativado' };

            const reg = await navigator.serviceWorker.ready;
            let sub = await reg.pushManager.getSubscription();
            if (!sub) {
                sub = await reg.pushManager.subscribe({
                    userVisibleOnly: true,
                    applicationServerKey: base64ParaBytes(chavePublica)
                });
            }
            const json = sub.toJSON();
            return { endpoint: json.endpoint, p256dh: json.keys.p256dh, auth: json.keys.auth };
        } catch (e) {
            return { erro: 'falha', detalhe: String(e && e.message || e) };
        }
    }

    /** Inscrição atual (para reenviar ao servidor quando abre o app). */
    async function inscricaoAtual() {
        try {
            if (!('serviceWorker' in navigator)) return null;
            const reg = await navigator.serviceWorker.ready;
            const sub = await reg.pushManager.getSubscription();
            if (!sub) return null;
            const json = sub.toJSON();
            return { endpoint: json.endpoint, p256dh: json.keys.p256dh, auth: json.keys.auth };
        } catch { return null; }
    }

    async function desativarPush() {
        try {
            const reg = await navigator.serviceWorker.ready;
            const sub = await reg.pushManager.getSubscription();
            if (!sub) return null;
            const endpoint = sub.endpoint;
            await sub.unsubscribe();
            return endpoint;
        } catch { return null; }
    }

    // ── Gravação de áudio (mesmo formato do gravador do HELP: Opus) ──────────
    let gravador = null, pedacos = [], fluxo = null;

    /** Retorna 'ok' | 'inseguro' | 'negado' | 'sem-microfone' | 'falha' */
    async function audioIniciar() {
        if (!window.isSecureContext || !navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) return 'inseguro';
        if (typeof MediaRecorder === 'undefined') return 'falha';
        try {
            fluxo = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true } });
        } catch (e) {
            if (e && (e.name === 'NotAllowedError' || e.name === 'SecurityError')) return 'negado';
            if (e && e.name === 'NotFoundError') return 'sem-microfone';
            return 'falha';
        }
        const tipo = ['audio/ogg;codecs=opus', 'audio/webm;codecs=opus', 'audio/webm', 'audio/mp4']
            .find(m => MediaRecorder.isTypeSupported(m)) || '';
        pedacos = [];
        gravador = new MediaRecorder(fluxo, tipo ? { mimeType: tipo, audioBitsPerSecond: 32000 } : {});
        gravador.ondataavailable = e => { if (e.data && e.data.size > 0) pedacos.push(e.data); };
        gravador.start(250);
        vibrar();
        return 'ok';
    }

    function liberarMicrofone() {
        try { fluxo && fluxo.getTracks().forEach(t => t.stop()); } catch { }
        fluxo = null;
    }

    /** Para e devolve { base64, mimeType } (ou null se não estava gravando). */
    function audioParar() {
        return new Promise(resolve => {
            if (!gravador || gravador.state === 'inactive') { liberarMicrofone(); resolve(null); return; }
            const g = gravador;
            g.onstop = () => {
                const tipo = g.mimeType || 'audio/webm';
                const blob = new Blob(pedacos, { type: tipo });
                gravador = null; pedacos = [];
                liberarMicrofone();
                const leitor = new FileReader();
                leitor.onloadend = () => {
                    const dataUrl = String(leitor.result || '');
                    resolve({ base64: dataUrl.substring(dataUrl.indexOf(',') + 1), mimeType: tipo });
                };
                leitor.readAsDataURL(blob);
            };
            g.stop();
        });
    }

    function audioCancelar() {
        try { if (gravador && gravador.state !== 'inactive') { gravador.onstop = null; gravador.stop(); } } catch { }
        gravador = null; pedacos = [];
        liberarMicrofone();
    }

    function descricaoDispositivo() {
        const ua = navigator.userAgent;
        if (/android/i.test(ua)) return 'Android';
        if (ehIOS()) return 'iPhone/iPad';
        if (/windows/i.test(ua)) return 'Windows';
        if (/mac/i.test(ua)) return 'Mac';
        return 'Navegador';
    }

    return {
        get, set, del,
        registrarPagina, liberarPagina, visivel, onNavegar,
        rolarFim, pertoDoFim, rolarAte, observarRolagem, pararRolagem, alturaRolagem, manterPosicao,
        autoAltura, focar, limpar,
        badge, vibrar, copiar,
        situacaoPush, ativarPush, inscricaoAtual, desativarPush, descricaoDispositivo,
        audioIniciar, audioParar, audioCancelar
    };
})();
