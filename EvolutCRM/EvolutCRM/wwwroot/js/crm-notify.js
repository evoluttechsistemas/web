// wwwroot/js/crm-notify.js
// ✅ Notificações + Toast + Blink + Badge + Beep (com desbloqueio de áudio)
// ✅ Protegido contra "já declarado" (tudo fica em window.__crm*)
// ✅ Pronto para ser referenciado no App.razor: <script src="/js/crm-notify.js"></script>

(function () {
    // ============================
    // Helpers internos (globais, sem redeclarar)
    // ============================
    window.__crm = window.__crm || {};
    const CRM = window.__crm;

    // ============================
    // 🔔 NOTIFICATION PERMISSION
    // ============================
    window.crmNotifyInit = async function () {
        try {
            if (!("Notification" in window)) return "unsupported";
            if (Notification.permission === "granted") return "granted";
            if (Notification.permission === "denied") return "denied";
            const perm = await Notification.requestPermission();
            return perm;
        } catch (e) {
            console.warn("crmNotifyInit falhou:", e);
            return "error";
        }
    };

    // Popup do Windows (fora do navegador quando minimizado)
    // url: tela aberta ao clicar (ex.: /ticket/123). tag: avisos com a mesma tag se substituem
    // (o mesmo ticket); sem tag, cada aviso fica separado — antes todos usavam a mesma tag e
    // um aviso de card/chat/agenda apagava na hora o aviso de ticket que tinha acabado de aparecer.
    window.crmNotifyPopup = async function (title, body, iconUrl, url, tag) {
        // Ticket aberto em janela separada do HelpDesktop: quem avisa é a janela principal
        if (window.__helpJanelaAvulsa) return { ok: false, reason: "janela-separada" };
        try {
            if (!("Notification" in window)) {
                window.crmToast((title || "CRM") + " - " + (body || ""), "danger");
                return { ok: false, reason: "unsupported" };
            }

            if (Notification.permission !== "granted") {
                // Sem clique do usuário o navegador ignora o pedido de permissão; ele é feito no primeiro clique (abaixo)
                const perm = Notification.permission;

                if (perm !== "granted") {
                    window.crmToast((title || "CRM") + " - " + (body || ""), "danger");
                    return { ok: false, reason: "permission_" + perm };
                }
            }

            const n = new Notification(title || "CRM", {
                body: body || "",
                icon: iconUrl || "/images/logo-evoluttech.png",
                silent: false,
                tag: tag || ("evolutcrm-" + Date.now() + "-" + Math.random().toString(36).slice(2)),
                renotify: true
            });

            window.__crm.lastNotification = n;

            n.onclick = () => {
                try { window.focus(); } catch { }
                try { if (url && window.Blazor && window.Blazor.navigateTo) window.Blazor.navigateTo(url); } catch { }
                try { n.close(); } catch { }
                window.__crm.lastNotification = null;
            };

            n.onclose = () => {
                if (window.__crm.lastNotification === n) {
                    window.__crm.lastNotification = null;
                }
            };

            // Não fecha mais sozinho em 8s: quem não estava olhando perdia o aviso (sumia também da
            // central de notificações do Windows). O Windows esconde o balão e guarda na central.

            return { ok: true };
        } catch (e) {
            console.warn("crmNotifyPopup falhou:", e);
            window.crmToast("⚠️ Falha ao disparar notificação do Windows.", "danger");
            return { ok: false, reason: "exception" };
        }
    };


    // ============================
    // 🟦 BADGE (Taskbar / AppBadge)
    // ============================
    window.crmSetBadge = async function (count) {
        try { window.crmDesktopContador && window.crmDesktopContador("tickets", count); } catch { }
        try {
            // Chromium/Edge: App Badge API (nem sempre habilitada)
            if ("setAppBadge" in navigator) {
                if (!count || count <= 0) await navigator.clearAppBadge();
                else await navigator.setAppBadge(count);
            }
        } catch (e) {
            // ignora
        }
    };

    // ============================
    // ✨ BLINK (piscar título da aba)
    // ============================
    window.crmBlinkStart = function (message, seconds) {
        try {
            window.crmBlinkStop();

            CRM.oldTitle = document.title;
            const m = message || "🔴 NOVO TICKET!";
            const totalMs = (seconds || 8) * 1000;

            let flag = false;
            CRM.blinkTimer = setInterval(() => {
                document.title = flag ? m : CRM.oldTitle;
                flag = !flag;
            }, 650);

            CRM.blinkStopTimer = setTimeout(() => window.crmBlinkStop(), totalMs);
        } catch (e) {
            console.warn("crmBlinkStart falhou:", e);
        }
    };

    window.crmBlinkStop = function () {
        try {
            if (CRM.blinkTimer) {
                clearInterval(CRM.blinkTimer);
                CRM.blinkTimer = null;
            }
            if (CRM.blinkStopTimer) {
                clearTimeout(CRM.blinkStopTimer);
                CRM.blinkStopTimer = null;
            }
            if (CRM.oldTitle) document.title = CRM.oldTitle;
        } catch (e) {
            // ignora
        }
    };

    // ============================
    // 🔊 ÁUDIO: Unlock + Beep
    // (Chrome/Edge bloqueia áudio até 1 gesto do usuário)
    // ============================
    window.crmAudioUnlock = async function () {
        try {
            const AudioContext = window.AudioContext || window.webkitAudioContext;
            if (!AudioContext) return false;

            if (!CRM.audioCtx) CRM.audioCtx = new AudioContext();

            if (CRM.audioCtx.state === "suspended") {
                await CRM.audioCtx.resume();
            }

            // toque mudo curtinho para "liberar"
            const ctx = CRM.audioCtx;
            const o = ctx.createOscillator();
            const g = ctx.createGain();
            o.connect(g);
            g.connect(ctx.destination);
            g.gain.value = 0.00001;
            o.start();
            o.stop(ctx.currentTime + 0.03);

            CRM.audioUnlocked = true;
            return true;
        } catch (e) {
            console.warn("crmAudioUnlock falhou:", e);
            CRM.audioUnlocked = false;
            return false;
        }
    };

    // Beep: só toca se já estiver desbloqueado
    window.crmBeep = function (ms) {
        if (window.__helpJanelaAvulsa) return;
        try {
            const duration = ms || 250;
            const AudioContext = window.AudioContext || window.webkitAudioContext;
            if (!AudioContext) return;

            if (!CRM.audioCtx) CRM.audioCtx = new AudioContext();

            // Áudio "suspenso" (ninguém clicou ainda): tenta liberar e toca em seguida.
            // No HelpDesktop o autoplay é liberado, então isso funciona mesmo sem clique.
            if (CRM.audioCtx.state === "suspended") {
                CRM.audioCtx.resume()
                    .then(() => { if (CRM.audioCtx.state === "running") window.crmBeep(duration); })
                    .catch(() => { });
                return;
            }

            const ctx = CRM.audioCtx;

            const o = ctx.createOscillator();
            const g = ctx.createGain();
            o.connect(g);
            g.connect(ctx.destination);

            o.type = "sine";
            o.frequency.value = 880;

            g.gain.setValueAtTime(0.0001, ctx.currentTime);
            g.gain.exponentialRampToValueAtTime(0.15, ctx.currentTime + 0.02);

            // Fim agendado no próprio relógio do áudio (não depende de timer, que atrasa em segundo plano)
            const fim = ctx.currentTime + duration / 1000;
            o.start();
            g.gain.setValueAtTime(0.15, fim);
            g.gain.exponentialRampToValueAtTime(0.0001, fim + 0.02);
            o.stop(fim + 0.04);
        } catch (e) {
            console.warn("crmBeep falhou:", e);
        }
    };

    // Auto-unlock no primeiro clique/tecla
    (function attachAudioUnlock() {
        const unlockOnce = async () => {
            // Permissão do popup do Windows: o navegador só aceita o pedido junto de um clique/tecla
            try {
                if ("Notification" in window && Notification.permission === "default")
                    Notification.requestPermission();
            } catch { }
            await window.crmAudioUnlock();
            window.removeEventListener("pointerdown", unlockOnce);
            window.removeEventListener("keydown", unlockOnce);
        };

        // Evita anexar mais de uma vez
        if (CRM.audioUnlockListenersAttached) return;
        CRM.audioUnlockListenersAttached = true;

        window.addEventListener("pointerdown", unlockOnce, { once: true });
        window.addEventListener("keydown", unlockOnce, { once: true });
    })();

    // ============================
    // 🍞 TOAST (visual dentro do sistema)
    // ============================
    window.crmToast = function (text, type) {
        try {
            const id = "crm-toast-host";
            let host = document.getElementById(id);

            if (!host) {
                host = document.createElement("div");
                host.id = id;
                host.style.position = "fixed";
                host.style.right = "18px";
                host.style.bottom = "18px";
                host.style.zIndex = 999999;
                host.style.display = "flex";
                host.style.flexDirection = "column";
                host.style.gap = "10px";
                document.body.appendChild(host);
            }

            const el = document.createElement("div");
            el.style.minWidth = "260px";
            el.style.maxWidth = "360px";
            el.style.padding = "12px 14px";
            el.style.borderRadius = "14px";
            el.style.boxShadow = "0 12px 26px rgba(0,0,0,.18)";
            el.style.fontFamily = "Inter, system-ui, sans-serif";
            el.style.fontWeight = "800";
            el.style.fontSize = "14px";
            el.style.cursor = "pointer";
            el.style.transition = "transform .2s ease, opacity .2s ease";
            el.style.opacity = "0";
            el.style.transform = "translateY(8px)";

            if ((type || "info") === "danger") {
                el.style.background = "linear-gradient(135deg,#fee2e2,#fecaca,#fff)";
                el.style.border = "1px solid #dc2626";
                el.style.color = "#7f1d1d";
            } else {
                el.style.background = "linear-gradient(135deg,#dbeafe,#bfdbfe,#fff)";
                el.style.border = "1px solid #2563eb";
                el.style.color = "#1e3a8a";
            }

            el.innerText = text || "Notificação";
            host.appendChild(el);

            requestAnimationFrame(() => {
                el.style.opacity = "1";
                el.style.transform = "translateY(0)";
            });

            const kill = () => {
                el.style.opacity = "0";
                el.style.transform = "translateY(8px)";
                setTimeout(() => el.remove(), 200);
            };

            el.onclick = kill;
            setTimeout(kill, 4500);
        } catch (e) {
            console.warn("crmToast falhou:", e);
        }
    };


    // ============================
    // ✅ DEBUG opcional
    // ============================
    window.crmNotifyDebug = function () {
        try {
            return {
                notificationSupported: ("Notification" in window),
                notificationPermission: ("Notification" in window) ? Notification.permission : "unsupported",
                audioContextSupported: !!(window.AudioContext || window.webkitAudioContext),
                audioState: CRM.audioCtx ? CRM.audioCtx.state : "none",
                audioUnlocked: !!CRM.audioUnlocked
            };
        } catch {
            return { error: true };
        }
    };
})();

// ============================
// 🖥️ HelpDesktop (programa instalado)
// ============================
(function () {
    const webview = (window.chrome && window.chrome.webview) || null;
    const contadores = { tickets: 0, chat: 0 };
    let layoutRef = null;

    function paraDesktop(msg) {
        try { if (webview && !window.__helpJanelaAvulsa) webview.postMessage(msg); } catch { }
    }

    // Está dentro do HelpDesktop (janela principal)?
    window.crmNoDesktop = function () { return !!webview && !window.__helpJanelaAvulsa; };

    // Número de não lidos no ícone da barra de tarefas (tickets novos + chat interno)
    window.crmDesktopContador = function (tipo, n) {
        contadores[tipo] = Math.max(0, Number(n) || 0);
        paraDesktop({ type: "contador", total: contadores.tickets + contadores.chat });
    };

    window.crmRegistrarLayout = function (ref) { layoutRef = ref; };

    window.crmAbrirJanelaSeparada = function (rota) { paraDesktop({ type: "janela", url: String(rota || "") }); };

    if (!webview) return;

    webview.addEventListener("message", async (ev) => {
        const m = ev.data || {};

        // Resposta escrita no próprio aviso do Windows
        if (m.type === "responder") {
            const codigo = Number(m.codigo) || 0;
            let r = { ok: false, mensagem: "O HELP ainda está carregando. Abra o ticket para responder." };
            try {
                if (layoutRef) r = await layoutRef.invokeMethodAsync("ResponderPeloAviso", codigo, String(m.texto || ""));
            } catch (e) {
                r = { ok: false, mensagem: "Não foi possível enviar a resposta. Abra o ticket para responder." };
            }
            paraDesktop({ type: "resposta-aviso", ok: !!(r && r.ok), texto: (r && r.mensagem) || "", codigo });
            return;
        }

        // Print de área (Ctrl+Alt+P): vai para o ticket aberto
        if (m.type === "print") {
            let ok = false;
            try { ok = window.crmColarImagemNoTicket ? await window.crmColarImagemNoTicket(m.imagem) : false; } catch { }
            paraDesktop({ type: "print-resultado", ok: !!ok });
        }
    });
})();
