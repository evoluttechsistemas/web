// Toque de "chamada recebida" + notificação do Windows para convites de reunião
let ctx = null;
let timer = null;

function contexto() {
    if (!ctx) {
        const Ctx = window.AudioContext || window.webkitAudioContext;
        if (!Ctx) return null;
        ctx = new Ctx();
    }
    if (ctx.state === "suspended") ctx.resume().catch(() => { });
    return ctx;
}

// Três notas suaves (tipo marimba), repetidas a cada 2,6 s
function tocarUmaVez() {
    const c = contexto();
    if (!c) return;
    const t = c.currentTime;
    [[784, 0], [988, 0.16], [1175, 0.32], [784, 0.9], [988, 1.06], [1175, 1.22]].forEach(([f, d]) => {
        const o = c.createOscillator();
        const g = c.createGain();
        o.type = "triangle";
        o.frequency.value = f;
        g.gain.setValueAtTime(0.0001, t + d);
        g.gain.exponentialRampToValueAtTime(0.09, t + d + 0.015);
        g.gain.exponentialRampToValueAtTime(0.0001, t + d + 0.38);
        o.connect(g).connect(c.destination);
        o.start(t + d);
        o.stop(t + d + 0.4);
    });
}

export function tocarToque() {
    if (timer) return;
    tocarUmaVez();
    timer = setInterval(tocarUmaVez, 2600);
}

export function pararToque() {
    if (timer) clearInterval(timer);
    timer = null;
}

export function notificar(titulo, corpo) {
    try { window.crmNotifyPopup?.(titulo, corpo, "/images/favicon.ico"); } catch { }
    try { window.crmBlinkStart?.("📹 Convite de reunião", 30); } catch { }
}

export function pararAvisos() {
    pararToque();
    try { window.crmBlinkStop?.(); } catch { }
}
