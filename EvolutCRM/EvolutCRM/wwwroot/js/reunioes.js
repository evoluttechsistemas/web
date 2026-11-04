// =====================================================================
// Reuniões do HELP — WebRTC em malha (até 6 pessoas), sinalização via SignalR
// O áudio/vídeo vai direto entre os navegadores; o servidor só troca os sinais.
// =====================================================================
const SIGNALR_URL = "/lib/signalr/signalr.min.js";

const rtcConfig = {
    iceServers: [
        { urls: ["stun:stun.l.google.com:19302", "stun:stun1.l.google.com:19302"] }
        // Produção (redes diferentes / 4G): inclua seu servidor coturn.
        // { urls: "turn:turn.seudominio.com:3478", username: "USUARIO", credential: "SENHA" }
    ]
};

let hub = null;
let localStream = null;      // microfone + câmera
let screenStream = null;     // compartilhamento de tela
let tokenAtual = "";
let dotNetRef = null;
let donoAtual = "";          // id da aba (componente) dona da chamada/prévia
let meuUsuario = "";
let maoLevantada = false;
let fixadoId = null;
let inicioChamada = 0;
let timerDuracao = null;
let timerFala = null;
let audioCtx = null;
let previaAtiva = false;
let previaNivelTimer = null;
const peers = new Map();
const estadosPendentes = new Map();
const medidores = new Map();
const LOCAL = "local";

// ---------------------------------------------------------------------
// Utilitários
// ---------------------------------------------------------------------
const CORES = ["#1768DF", "#0F8F7E", "#7C4DDB", "#C2410C", "#B4236C", "#2F7D32", "#0E7490", "#9A6B00", "#5B5BD6", "#A23B3B"];

function corUsuario(usuario) {
    const u = (usuario || "").trim().toUpperCase();
    let h = 0;
    for (let i = 0; i < u.length; i++) h = (h * 31 + u.charCodeAt(i)) % 100000;
    return CORES[h % CORES.length];
}

function iniciais(nome) {
    const p = (nome || "").replace(/[._]/g, " ").split(" ").filter(Boolean);
    if (!p.length) return "?";
    if (p.length === 1) return p[0].substring(0, 2).toUpperCase();
    return (p[0][0] + p[p.length - 1][0]).toUpperCase();
}

function nomeBonito(usuario) {
    return (usuario || "").trim().toLowerCase().split(" ").filter(Boolean)
        .map(p => p[0].toUpperCase() + p.substring(1)).join(" ");
}

function avisar(metodo, ...args) {
    try { dotNetRef?.invokeMethodAsync(metodo, ...args); } catch { }
}

function mensagemErro(e) {
    if (e?.name === "NotAllowedError") return "permissão negada no navegador";
    if (e?.name === "NotFoundError") return "dispositivo não encontrado";
    if (e?.name === "NotReadableError") return "dispositivo em uso por outro programa";
    return (e?.message ?? String(e)).replace(/^.*HubException: /, "");
}

function carregarSignalR() {
    if (window.signalR) return Promise.resolve();
    return new Promise((resolve, reject) => {
        const existente = document.querySelector(`script[src="${SIGNALR_URL}"]`);
        const script = existente ?? document.createElement("script");
        script.addEventListener("load", () => resolve(), { once: true });
        script.addEventListener("error", () => reject(new Error("Não foi possível carregar a biblioteca SignalR.")), { once: true });
        if (!existente) {
            script.src = SIGNALR_URL;
            document.head.appendChild(script);
        }
    });
}

function contextoAudio() {
    if (!audioCtx) {
        const Ctx = window.AudioContext || window.webkitAudioContext;
        if (!Ctx) return null;
        audioCtx = new Ctx();
    }
    if (audioCtx.state === "suspended") audioCtx.resume().catch(() => { });
    return audioCtx;
}

// Som curto de entrada/saída (sem arquivos de áudio)
function tocarSinal(subindo) {
    const ctx = contextoAudio();
    if (!ctx) return;
    const t = ctx.currentTime;
    const notas = subindo ? [660, 880] : [660, 440];
    notas.forEach((f, i) => {
        const o = ctx.createOscillator();
        const g = ctx.createGain();
        o.type = "sine";
        o.frequency.value = f;
        g.gain.setValueAtTime(0.0001, t + i * 0.12);
        g.gain.exponentialRampToValueAtTime(0.06, t + i * 0.12 + 0.02);
        g.gain.exponentialRampToValueAtTime(0.0001, t + i * 0.12 + 0.18);
        o.connect(g).connect(ctx.destination);
        o.start(t + i * 0.12);
        o.stop(t + i * 0.12 + 0.2);
    });
}

function microfoneLigado() {
    const a = localStream?.getAudioTracks()[0];
    return !!a && a.enabled && a.readyState === "live";
}
function cameraLigada() { return !!localStream?.getVideoTracks().length; }
function faixaVideoAtual() {
    return screenStream?.getVideoTracks()[0] ?? localStream?.getVideoTracks()[0] ?? null;
}
function meuEstado() {
    return { mic: microfoneLigado(), cam: cameraLigada(), tela: !!screenStream, mao: maoLevantada };
}

async function obterAudio(deviceId) {
    return navigator.mediaDevices.getUserMedia({
        audio: {
            deviceId: deviceId ? { exact: deviceId } : undefined,
            echoCancellation: true, noiseSuppression: true, autoGainControl: true
        },
        video: false
    });
}

async function obterVideo(deviceId) {
    return navigator.mediaDevices.getUserMedia({
        video: {
            deviceId: deviceId ? { exact: deviceId } : undefined,
            width: { ideal: 1280 }, height: { ideal: 720 }, frameRate: { ideal: 24, max: 30 }
        },
        audio: false
    });
}

export async function listarDispositivos() {
    try {
        const lista = await navigator.mediaDevices.enumerateDevices();
        const map = (tipo, padrao) => lista.filter(d => d.kind === tipo)
            .map((d, i) => ({ id: d.deviceId, nome: d.label || `${padrao} ${i + 1}` }));
        return {
            microfones: map("audioinput", "Microfone"),
            cameras: map("videoinput", "Câmera"),
            micAtual: localStream?.getAudioTracks()[0]?.getSettings()?.deviceId ?? "",
            camAtual: localStream?.getVideoTracks()[0]?.getSettings()?.deviceId ?? ""
        };
    } catch {
        return { microfones: [], cameras: [], micAtual: "", camAtual: "" };
    }
}

// ---------------------------------------------------------------------
// Prévia (tela antes de entrar)
// ---------------------------------------------------------------------
export async function previaIniciar(dono, opcoes) {
    if (hub && donoAtual && donoAtual !== dono)
        return { emOutraReuniao: true, mic: false, cam: false, erro: "" };

    donoAtual = dono;
    previaAtiva = true;
    if (!localStream) localStream = new MediaStream();
    let erro = "";

    if (opcoes?.mic !== false && !localStream.getAudioTracks().length) {
        try { (await obterAudio(opcoes?.micId)).getAudioTracks().forEach(t => localStream.addTrack(t)); }
        catch (e) { erro = "Microfone: " + mensagemErro(e); }
    }
    if (opcoes?.cam && !localStream.getVideoTracks().length) {
        try { (await obterVideo(opcoes?.camId)).getVideoTracks().forEach(t => localStream.addTrack(t)); }
        catch (e) { erro = (erro ? erro + " · " : "") + "Câmera: " + mensagemErro(e); }
    }

    atualizarPrevia();
    iniciarNivelPrevia();
    return { emOutraReuniao: false, mic: microfoneLigado(), cam: cameraLigada(), erro };
}

export async function previaAlternar(tipo) {
    if (tipo === "mic") {
        const a = localStream?.getAudioTracks()[0];
        if (a) a.enabled = !a.enabled;
        else {
            if (!localStream) localStream = new MediaStream();
            (await obterAudio()).getAudioTracks().forEach(t => localStream.addTrack(t));
        }
    } else {
        if (cameraLigada()) pararCamera();
        else {
            if (!localStream) localStream = new MediaStream();
            (await obterVideo()).getVideoTracks().forEach(t => localStream.addTrack(t));
        }
    }
    atualizarPrevia();
    iniciarNivelPrevia();
    return { mic: microfoneLigado(), cam: cameraLigada() };
}

export async function previaParar(dono) {
    if (hub || (donoAtual && donoAtual !== dono)) return;
    pararNivelPrevia();
    localStream?.getTracks().forEach(t => t.stop());
    localStream = null;
    previaAtiva = false;
    donoAtual = "";
}

function atualizarPrevia() {
    const video = document.getElementById("rn-previa-video");
    if (!video) return;
    const v = localStream?.getVideoTracks()[0];
    video.srcObject = v ? new MediaStream([v]) : null;
    video.closest(".rn-previa-video")?.classList.toggle("sem-video", !v);
    if (v) video.play().catch(() => { });
}

function iniciarNivelPrevia() {
    pararNivelPrevia();
    const a = localStream?.getAudioTracks()[0];
    const ctx = contextoAudio();
    if (!a || !ctx) return;
    const analyser = ctx.createAnalyser();
    analyser.fftSize = 512;
    ctx.createMediaStreamSource(new MediaStream([a])).connect(analyser);
    const dados = new Uint8Array(analyser.frequencyBinCount);
    previaNivelTimer = setInterval(() => {
        const barra = document.getElementById("rn-previa-nivel");
        if (!barra) return;
        analyser.getByteFrequencyData(dados);
        const media = dados.reduce((s, v) => s + v, 0) / dados.length;
        barra.style.setProperty("--nivel", a.enabled ? Math.min(100, media * 2.2).toFixed(0) + "%" : "0%");
    }, 90);
}

function pararNivelPrevia() {
    if (previaNivelTimer) clearInterval(previaNivelTimer);
    previaNivelTimer = null;
}

// ---------------------------------------------------------------------
// Entrar / sair
// ---------------------------------------------------------------------
export async function entrar(token, referenciaDotNet, dono, usuario) {
    // Só uma reunião por vez: se outra aba estava em chamada, ela é avisada e encerrada
    if (hub && donoAtual && donoAtual !== dono) {
        avisar("ReuniaoEncerradaPorOutraAba");
        await encerrar();
    } else if (hub) {
        await encerrarChamada(true);
    }

    pararNivelPrevia();
    previaAtiva = false;
    tokenAtual = token;
    dotNetRef = referenciaDotNet;
    donoAtual = dono;
    meuUsuario = (usuario || "").toUpperCase();
    maoLevantada = false;
    fixadoId = null;
    if (!localStream) localStream = new MediaStream();
    // Se a prévia não pegou o microfone (ex.: estava em outra reunião), tenta agora
    if (!localStream.getAudioTracks().length) {
        try { (await obterAudio()).getAudioTracks().forEach(t => localStream.addTrack(t)); } catch { }
    }

    await carregarSignalR();

    hub = new window.signalR.HubConnectionBuilder()
        .withUrl("/hubs/reuniao")
        .withAutomaticReconnect([0, 2000, 5000, 10000, 15000, 30000])
        .build();

    hub.on("participanteEntrou", async (connectionId, usuarioRemoto, estado) => {
        if (estado) estadosPendentes.set(connectionId, estado);
        try { await criarOferta(connectionId, usuarioRemoto); }
        catch (e) { console.warn("[Reunião] oferta", e); }
        enviarEstado();
        tocarSinal(true);
        avisar("Aviso", `${nomeBonito(usuarioRemoto)} entrou na reunião`, "entrou");
        avisar("AtualizarParticipantes");
    });

    hub.on("participanteSaiu", (connectionId, usuarioRemoto) => {
        if (!peers.has(connectionId)) return;
        removerPeer(connectionId);
        tocarSinal(false);
        avisar("Aviso", `${nomeBonito(usuarioRemoto)} saiu da reunião`, "saiu");
        avisar("AtualizarParticipantes");
    });

    hub.on("receberSinal", (remetente, usuarioRemoto, sinal) => {
        // Sinais de cada participante em fila (ICE pode chegar antes da oferta terminar)
        const peer = peers.get(remetente) ?? criarPeer(remetente, usuarioRemoto);
        peer.fila = peer.fila.then(() => processarSinal(peer, sinal)).catch(e => console.warn("[Reunião] sinal", e));
    });

    hub.on("estadoParticipante", (connectionId, usuarioRemoto, estado) => {
        const peer = peers.get(connectionId);
        if (!peer) { estadosPendentes.set(connectionId, estado); return; }
        const levantouAgora = estado?.mao && !peer.estado?.mao;
        peer.estado = estado;
        aplicarEstadoTile(connectionId, estado);
        if (levantouAgora) avisar("Aviso", `${nomeBonito(usuarioRemoto)} levantou a mão`, "mao");
    });

    hub.on("reacao", (connectionId, usuarioRemoto, emoji) => {
        mostrarReacao(connectionId === hub?.connectionId ? LOCAL : connectionId, emoji);
    });

    hub.on("receberChat", mensagem => avisar("ReceberMensagemRealtime", mensagem));

    hub.on("reuniaoEncerrada", async () => {
        const ref = dotNetRef;
        await encerrar();
        try { ref?.invokeMethodAsync("ReuniaoEncerradaPeloOrganizador"); } catch { }
    });

    hub.onreconnecting(() => avisar("AtualizarEstadoConexao", "reconectando"));

    hub.onreconnected(async () => {
        // O connectionId muda ao reconectar: refaz as conexões de mídia do zero
        for (const id of [...peers.keys()]) removerPeer(id);
        try {
            await hub.invoke("EntrarSala", tokenAtual, meuEstado());
            avisar("AtualizarEstadoConexao", "conectado");
        } catch (e) {
            avisar("AtualizarEstadoConexao", "falhou:" + mensagemErro(e));
        }
    });

    hub.onclose(() => { if (hub) avisar("AtualizarEstadoConexao", "desconectado"); });

    await hub.start();
    await hub.invoke("EntrarSala", tokenAtual, meuEstado());

    criarTile(LOCAL, meuUsuario, true);
    atualizarTileLocal();
    iniciarDuracao();
    iniciarDeteccaoFala();
    document.addEventListener("keydown", atalhos, true);

    avisar("AtualizarEstadoConexao", "conectado");
    publicarEstado();
}

export async function encerrarSeDono(dono) {
    if (donoAtual && donoAtual !== dono) return;
    await encerrar();
}

export async function encerrar() {
    await encerrarChamada(false);
}

async function encerrarChamada(manterMidia) {
    document.removeEventListener("keydown", atalhos, true);
    if (hub) {
        const h = hub;
        hub = null;
        try { if (h.state === "Connected") await h.invoke("SairSala"); } catch { }
        try { await h.stop(); } catch { }
    }
    for (const id of [...peers.keys()]) removerPeer(id);
    removerTile(LOCAL);
    estadosPendentes.clear();
    if (timerDuracao) clearInterval(timerDuracao);
    if (timerFala) clearInterval(timerFala);
    timerDuracao = null;
    timerFala = null;
    medidores.clear();
    screenStream?.getTracks().forEach(t => t.stop());
    screenStream = null;
    if (!manterMidia) {
        pararNivelPrevia();
        localStream?.getTracks().forEach(t => t.stop());
        localStream = null;
        tokenAtual = "";
        dotNetRef = null;
        donoAtual = "";
        previaAtiva = false;
    }
    maoLevantada = false;
    fixadoId = null;
}

// ---------------------------------------------------------------------
// Controles
// ---------------------------------------------------------------------
export async function alternarMicrofone() {
    const a = localStream?.getAudioTracks()[0];
    if (!a || a.readyState !== "live") {
        if (!localStream) localStream = new MediaStream();
        if (a) localStream.removeTrack(a);
        const nova = (await obterAudio()).getAudioTracks()[0];
        localStream.addTrack(nova);
        await aplicarFaixaEmTodos("audio", nova);
        monitorarFala(LOCAL, new MediaStream([nova]));
    } else {
        a.enabled = !a.enabled;
    }
    publicarEstado();
    return microfoneLigado();
}

export async function alternarCamera() {
    if (cameraLigada()) {
        pararCamera();
        if (!screenStream) await aplicarFaixaEmTodos("video", null);
    } else {
        const faixa = (await obterVideo()).getVideoTracks()[0];
        if (!localStream) localStream = new MediaStream();
        localStream.addTrack(faixa);
        if (!screenStream) await aplicarFaixaEmTodos("video", faixa);
    }
    atualizarTileLocal();
    publicarEstado();
    return cameraLigada();
}

function pararCamera() {
    localStream?.getVideoTracks().forEach(t => { t.stop(); localStream.removeTrack(t); });
}

export async function compartilharTela() {
    if (screenStream) {
        await pararCompartilhamento();
        return false;
    }
    try {
        screenStream = await navigator.mediaDevices.getDisplayMedia({ video: { frameRate: { ideal: 15, max: 30 } }, audio: false });
    } catch (e) {
        if (e?.name === "NotAllowedError" || e?.name === "AbortError") return false; // cancelou a escolha
        throw e;
    }
    const faixa = screenStream.getVideoTracks()[0];
    try { faixa.contentHint = "detail"; } catch { }
    faixa.onended = () => pararCompartilhamento();
    await aplicarFaixaEmTodos("video", faixa);
    atualizarTileLocal();
    publicarEstado();
    return true;
}

async function pararCompartilhamento() {
    screenStream?.getTracks().forEach(t => t.stop());
    screenStream = null;
    await aplicarFaixaEmTodos("video", localStream?.getVideoTracks()[0] ?? null);
    atualizarTileLocal();
    publicarEstado();
}

export function alternarMao() {
    maoLevantada = !maoLevantada;
    atualizarTileLocal();
    publicarEstado();
    return maoLevantada;
}

export async function enviarReacao(emoji) {
    if (!hub || hub.state !== "Connected") return;
    try { await hub.invoke("EnviarReacao", emoji); } catch { }
}

export async function enviarChat(texto) {
    if (!hub || hub.state !== "Connected" || !texto?.trim()) return;
    await hub.invoke("EnviarChat", texto);
}

export async function trocarDispositivo(tipo, deviceId) {
    if (!localStream) localStream = new MediaStream();
    if (tipo === "mic") {
        const anterior = localStream.getAudioTracks()[0];
        const estavaLigado = anterior ? anterior.enabled : true;
        const nova = (await obterAudio(deviceId)).getAudioTracks()[0];
        nova.enabled = estavaLigado;
        if (anterior) { anterior.stop(); localStream.removeTrack(anterior); }
        localStream.addTrack(nova);
        await aplicarFaixaEmTodos("audio", nova);
        monitorarFala(LOCAL, new MediaStream([nova]));
    } else {
        const nova = (await obterVideo(deviceId)).getVideoTracks()[0];
        pararCamera();
        localStream.addTrack(nova);
        if (!screenStream) await aplicarFaixaEmTodos("video", nova);
    }
    if (previaAtiva) { atualizarPrevia(); iniciarNivelPrevia(); }
    else { atualizarTileLocal(); publicarEstado(); }
    return { mic: microfoneLigado(), cam: cameraLigada() };
}

export function rolarChat(id) {
    const el = document.getElementById(id);
    if (el) el.scrollTop = el.scrollHeight;
}

function publicarEstado() {
    const e = meuEstado();
    avisar("AtualizarMidiaLocal", e.mic, e.cam, e.tela, e.mao);
    enviarEstado();
}

function enviarEstado() {
    if (hub && hub.state === "Connected") hub.invoke("EnviarEstado", meuEstado()).catch(() => { });
}

// Ctrl+D microfone · Ctrl+E câmera (mesmos atalhos do Google Meet)
function atalhos(ev) {
    if (!ev.ctrlKey || ev.altKey || ev.shiftKey) return;
    const grid = document.getElementById("rn-grid");
    if (!grid || grid.offsetParent === null) return;   // aba da reunião não está visível
    const k = ev.key.toLowerCase();
    if (k === "d") { ev.preventDefault(); alternarMicrofone().catch(() => { }); }
    if (k === "e") { ev.preventDefault(); alternarCamera().catch(() => { }); }
}

function iniciarDuracao() {
    inicioChamada = Date.now();
    if (timerDuracao) clearInterval(timerDuracao);
    timerDuracao = setInterval(() => {
        const el = document.getElementById("rn-duracao");
        if (!el) return;
        const s = Math.floor((Date.now() - inicioChamada) / 1000);
        const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), seg = s % 60;
        el.textContent = (h ? h + ":" + String(m).padStart(2, "0") : String(m).padStart(2, "0")) + ":" + String(seg).padStart(2, "0");
    }, 1000);
}

// ---------------------------------------------------------------------
// WebRTC
// Cada peer tem 1 transceiver de áudio e 1 de vídeo desde o início:
// ligar/desligar câmera ou tela só troca a faixa (replaceTrack), sem renegociar.
// ---------------------------------------------------------------------
async function criarOferta(connectionId, usuario) {
    if (peers.has(connectionId) || !hub) return;
    const peer = criarPeer(connectionId, usuario);
    const pc = peer.connection;
    pc.addTransceiver(localStream?.getAudioTracks()[0] ?? "audio", { direction: "sendrecv" });
    pc.addTransceiver(faixaVideoAtual() ?? "video", { direction: "sendrecv" });
    const oferta = await pc.createOffer();
    await pc.setLocalDescription(oferta);
    await hub.invoke("EnviarSinal", connectionId, { tipo: "oferta", sdp: pc.localDescription });
}

async function processarSinal(peer, sinal) {
    const pc = peer.connection;

    if (sinal.tipo === "oferta") {
        await pc.setRemoteDescription(new RTCSessionDescription(sinal.sdp));
        for (const t of pc.getTransceivers()) {
            const tipo = t.receiver.track?.kind;
            t.direction = "sendrecv";
            if (tipo === "audio") await t.sender.replaceTrack(localStream?.getAudioTracks()[0] ?? null);
            if (tipo === "video") await t.sender.replaceTrack(faixaVideoAtual());
        }
        const resposta = await pc.createAnswer();
        await pc.setLocalDescription(resposta);
        await hub?.invoke("EnviarSinal", peer.connectionId, { tipo: "resposta", sdp: pc.localDescription });
        await aplicarIcePendentes(peer);
        return;
    }

    if (sinal.tipo === "resposta") {
        await pc.setRemoteDescription(new RTCSessionDescription(sinal.sdp));
        await aplicarIcePendentes(peer);
        return;
    }

    if (sinal.tipo === "ice" && sinal.candidato) {
        if (!pc.remoteDescription) peer.icePendentes.push(sinal.candidato);
        else await pc.addIceCandidate(new RTCIceCandidate(sinal.candidato));
    }
}

async function aplicarIcePendentes(peer) {
    for (const c of peer.icePendentes.splice(0)) {
        try { await peer.connection.addIceCandidate(new RTCIceCandidate(c)); } catch (e) { console.warn("[Reunião] ICE", e); }
    }
}

function criarPeer(connectionId, usuario) {
    const connection = new RTCPeerConnection(rtcConfig);
    const peer = {
        connectionId,
        connection,
        usuario: usuario || "Participante",
        streamRemoto: new MediaStream(),
        estado: estadosPendentes.get(connectionId) ?? { mic: true, cam: false, tela: false, mao: false },
        icePendentes: [],
        fila: Promise.resolve()
    };
    estadosPendentes.delete(connectionId);
    peers.set(connectionId, peer);
    criarTile(connectionId, peer.usuario, false);
    aplicarEstadoTile(connectionId, peer.estado);

    connection.onicecandidate = ev => {
        if (ev.candidate && hub)
            hub.invoke("EnviarSinal", connectionId, { tipo: "ice", candidato: ev.candidate }).catch(() => { });
    };
    connection.ontrack = ev => {
        peer.streamRemoto.addTrack(ev.track);
        const tile = document.getElementById(`rn-tile-${connectionId}`);

        if (ev.track.kind === "audio") {
            // Áudio num <audio> próprio: toca mesmo com a câmera da pessoa desligada
            // (o <video> sem imagem não começa a tocar e prendia o som junto)
            const audio = tile?.querySelector("audio");
            if (audio) {
                audio.srcObject = new MediaStream([ev.track]);
                tocarAudio(audio);
            }
            monitorarFala(connectionId, new MediaStream([ev.track]));
        } else {
            // <video> mudo, só para a imagem
            const video = tile?.querySelector("video");
            if (video) {
                video.srcObject = new MediaStream([ev.track]);
                video.play().catch(() => { });
            }
        }
    };
    connection.onconnectionstatechange = () => {
        const tile = document.getElementById(`rn-tile-${connectionId}`);
        tile?.classList.toggle("conectando", ["new", "connecting"].includes(connection.connectionState));
        tile?.classList.toggle("instavel", connection.connectionState === "disconnected");
        if (["failed", "closed"].includes(connection.connectionState)) removerPeer(connectionId);
    };
    return peer;
}

// Toca o áudio; se o navegador bloquear (política de autoplay), tenta de novo no próximo clique/tecla
function tocarAudio(audio) {
    audio.play().catch(() => {
        const liberar = () => {
            document.querySelectorAll("#rn-grid audio").forEach(a => a.play().catch(() => { }));
            document.removeEventListener("click", liberar, true);
            document.removeEventListener("keydown", liberar, true);
        };
        document.addEventListener("click", liberar, true);
        document.addEventListener("keydown", liberar, true);
    });
}

async function aplicarFaixaEmTodos(tipo, faixa) {
    for (const peer of peers.values()) {
        const t = peer.connection.getTransceivers().find(x => x.receiver.track?.kind === tipo);
        if (t) {
            try { await t.sender.replaceTrack(faixa); } catch (e) { console.warn("[Reunião] replaceTrack", e); }
        }
    }
}

function removerPeer(connectionId) {
    const peer = peers.get(connectionId);
    if (!peer) return;
    try { peer.connection.close(); } catch { }
    peers.delete(connectionId);
    medidores.delete(connectionId);
    if (fixadoId === connectionId) fixadoId = null;
    removerTile(connectionId);
}

// ---------------------------------------------------------------------
// Grade de vídeos
// ---------------------------------------------------------------------
function criarTile(id, usuario, local) {
    const grid = document.getElementById("rn-grid");
    if (!grid || document.getElementById(`rn-tile-${id}`)) return;

    const nome = local ? "Você" : nomeBonito(usuario);
    const tile = document.createElement("div");
    tile.id = `rn-tile-${id}`;
    tile.className = "rn-tile sem-video" + (local ? " local" : " conectando");
    tile.dataset.id = id;
    tile.innerHTML = `
        <video autoplay playsinline muted></video>
        ${local ? "" : `<audio autoplay></audio>`}
        <div class="rn-tile-avatar"><span style="background:${corUsuario(usuario)}">${iniciais(nomeBonito(usuario))}</span></div>
        <div class="rn-tile-mao" title="Mão levantada"><i class="fa-solid fa-hand"></i></div>
        <div class="rn-tile-rodape">
            <span class="rn-tile-mic" title="Microfone desligado"><i class="fa-solid fa-microphone-slash"></i></span>
            <span class="rn-tile-nome"></span>
        </div>
        <button type="button" class="rn-tile-fixar" title="Destacar"><i class="fa-solid fa-thumbtack"></i></button>
        <div class="rn-tile-reacoes"></div>`;
    tile.querySelector(".rn-tile-nome").textContent = nome;
    tile.querySelector(".rn-tile-fixar").addEventListener("click", () => {
        fixadoId = fixadoId === id ? null : id;
        atualizarLayout();
    });

    if (local) grid.prepend(tile); else grid.append(tile);
    atualizarLayout();
}

function removerTile(id) {
    const tile = document.getElementById(`rn-tile-${id}`);
    if (!tile) return;
    tile.querySelectorAll("video, audio").forEach(m => { m.srcObject = null; });
    tile.remove();
    atualizarLayout();
}

function aplicarEstadoTile(id, estado) {
    const tile = document.getElementById(`rn-tile-${id}`);
    if (!tile || !estado) return;
    tile.classList.toggle("sem-video", !estado.cam && !estado.tela);
    tile.classList.toggle("mudo", !estado.mic);
    tile.classList.toggle("tela", !!estado.tela);
    tile.classList.toggle("mao", !!estado.mao);
    atualizarLayout();
}

function atualizarTileLocal() {
    const tile = document.getElementById(`rn-tile-${LOCAL}`);
    if (!tile) return;
    const video = tile.querySelector("video");
    const faixa = faixaVideoAtual();
    video.srcObject = faixa ? new MediaStream([faixa]) : null;
    if (faixa) video.play().catch(() => { });
    tile.classList.toggle("espelhar", !!faixa && !screenStream);
    aplicarEstadoTile(LOCAL, meuEstado());
}

function atualizarLayout() {
    const grid = document.getElementById("rn-grid");
    if (!grid) return;
    const tiles = [...grid.querySelectorAll(".rn-tile")];
    grid.dataset.qtd = String(Math.max(1, tiles.length));

    // Destaque: quem você fixou > quem está compartilhando a tela (exceto você mesmo)
    let destaque = fixadoId && document.getElementById(`rn-tile-${fixadoId}`) ? fixadoId : null;
    if (!destaque) {
        const compartilhando = tiles.find(t => t.classList.contains("tela") && t.dataset.id !== LOCAL);
        destaque = compartilhando?.dataset.id ?? null;
    }
    grid.classList.toggle("rn-grid--destaque", !!destaque && tiles.length > 1);
    tiles.forEach(t => {
        t.classList.toggle("destaque", t.dataset.id === destaque);
        t.classList.toggle("fixado", t.dataset.id === fixadoId);
    });
}

function mostrarReacao(id, emoji) {
    const area = document.querySelector(`#rn-tile-${CSS.escape(id)} .rn-tile-reacoes`);
    if (!area) return;
    const el = document.createElement("span");
    el.className = "rn-reacao";
    el.textContent = emoji;
    el.style.left = (20 + Math.random() * 60) + "%";
    area.append(el);
    setTimeout(() => el.remove(), 2600);
}

// ---------------------------------------------------------------------
// Quem está falando (contorno azul no vídeo)
// ---------------------------------------------------------------------
function monitorarFala(id, stream) {
    const ctx = contextoAudio();
    if (!ctx) return;
    try {
        const analyser = ctx.createAnalyser();
        analyser.fftSize = 512;
        analyser.smoothingTimeConstant = 0.6;
        ctx.createMediaStreamSource(stream).connect(analyser);
        medidores.set(id, { analyser, dados: new Uint8Array(analyser.frequencyBinCount), ate: 0 });
    } catch { }
}

function iniciarDeteccaoFala() {
    const a = localStream?.getAudioTracks()[0];
    if (a) monitorarFala(LOCAL, new MediaStream([a]));
    if (timerFala) clearInterval(timerFala);
    timerFala = setInterval(() => {
        const agora = Date.now();
        for (const [id, m] of medidores) {
            m.analyser.getByteFrequencyData(m.dados);
            const media = m.dados.reduce((s, v) => s + v, 0) / m.dados.length;
            const mudo = id === LOCAL ? !microfoneLigado() : peers.get(id)?.estado?.mic === false;
            if (!mudo && media > 14) m.ate = agora + 450;
            document.getElementById(`rn-tile-${id}`)?.classList.toggle("falando", m.ate > agora);
        }
    }, 150);
}
