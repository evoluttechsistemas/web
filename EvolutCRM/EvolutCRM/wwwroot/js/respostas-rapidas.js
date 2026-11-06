// Respostas rápidas na caixa de mensagem do ticket.
// Digite "/" (no começo ou depois de um espaço) e escolha a resposta: ↑ ↓ para navegar,
// Enter ou Tab para inserir, Esc para fechar. {cliente}, {primeiro_nome}, {atendente} e {saudacao}
// são trocados na hora.
(function () {
    const estado = new WeakMap();   // textarea -> { itens, ctx, ref, popup, filtrados, sel, inicio }

    function semAcento(s) {
        return (s || '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
    }

    function saudacao() {
        const h = new Date().getHours();
        return h < 12 ? 'Bom dia' : h < 18 ? 'Boa tarde' : 'Boa noite';
    }

    function capitalizar(s) {
        return (s || '').toLowerCase().replace(/(^|\s)\S/g, c => c.toUpperCase());
    }

    function resolver(texto, ctx) {
        const cliente = (ctx.cliente || '').trim();
        const primeiro = capitalizar(cliente.split(/\s+/)[0] || '');
        return texto
            .replace(/\{saudacao\}/gi, saudacao())
            .replace(/\{primeiro_nome\}/gi, primeiro)
            .replace(/\{cliente\}/gi, capitalizar(cliente))
            .replace(/\{atendente\}/gi, capitalizar(ctx.atendente || ''));
    }

    function escapar(s) {
        return (s || '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
    }

    function garantirEstilo() {
        if (document.getElementById('rr-estilo')) return;
        const st = document.createElement('style');
        st.id = 'rr-estilo';
        st.textContent = `
.rr-pop{position:fixed;z-index:3000;width:min(460px,calc(100vw - 24px));max-height:320px;display:flex;flex-direction:column;
  background:#fff;border:1px solid #e2e8f0;border-radius:14px;box-shadow:0 16px 40px rgba(15,23,42,.18);overflow:hidden;
  font-family:'IBM Plex Sans',system-ui,sans-serif;animation:rrIn .12s ease-out}
@keyframes rrIn{from{opacity:0;transform:translateY(6px)}to{opacity:1;transform:none}}
.rr-cab{display:flex;align-items:center;gap:8px;padding:8px 12px;font-size:11px;font-weight:700;color:#64748b;
  text-transform:uppercase;letter-spacing:.05em;border-bottom:1px solid #f1f5f9}
.rr-cab b{color:#0f766e}
.rr-lista{overflow:auto;padding:4px}
.rr-item{display:grid;grid-template-columns:auto 1fr;gap:2px 10px;padding:8px 10px;border-radius:10px;cursor:pointer}
.rr-item:hover,.rr-item.sel{background:#f0fdfa}
.rr-at{grid-row:span 2;align-self:start;font:700 12px/1 'JetBrains Mono',ui-monospace,monospace;color:#0f766e;
  background:#ccfbf1;border-radius:6px;padding:5px 7px}
.rr-ti{font-size:13px;font-weight:700;color:#0f172a}
.rr-tx{font-size:12px;color:#64748b;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.rr-vazio{padding:14px 12px;font-size:12px;color:#64748b}
.rr-rod{display:flex;justify-content:space-between;align-items:center;padding:6px 12px;border-top:1px solid #f1f5f9;
  font-size:11px;color:#94a3b8}
.rr-rod a{color:#0f766e;font-weight:700;text-decoration:none;cursor:pointer}`;
        document.head.appendChild(st);
    }

    function fechar(el) {
        const s = estado.get(el);
        if (!s || !s.popup) return;
        s.popup.remove();
        s.popup = null;
        s.filtrados = [];
    }

    // Texto entre a "/" e o cursor, se o cursor está num atalho
    function tokenAtual(el) {
        const pos = el.selectionStart;
        const antes = el.value.slice(0, pos);
        const m = antes.match(/(^|\s)\/([^\s\/]{0,30})$/);
        if (!m) return null;
        return { busca: m[2], inicio: pos - m[2].length - 1, fim: pos };
    }

    function filtrar(itens, busca) {
        const b = semAcento(busca);
        if (!b) return itens.slice(0, 8);
        const comeca = itens.filter(i => i.atalho.startsWith(b));
        const contem = itens.filter(i => !i.atalho.startsWith(b) &&
            (i.atalho.includes(b) || semAcento(i.titulo).includes(b) || semAcento(i.texto).includes(b)));
        return comeca.concat(contem).slice(0, 8);
    }

    function posicionar(el, pop) {
        const r = el.getBoundingClientRect();
        const alto = pop.offsetHeight || 200;
        const esquerda = Math.max(12, Math.min(r.left, window.innerWidth - pop.offsetWidth - 12));
        let topo = r.top - alto - 8;
        if (topo < 8) topo = r.bottom + 8;
        pop.style.left = esquerda + 'px';
        pop.style.top = topo + 'px';
    }

    function desenhar(el) {
        const s = estado.get(el);
        const tk = tokenAtual(el);
        if (!s || !tk) { fechar(el); return; }

        s.filtrados = filtrar(s.itens, tk.busca);
        if (s.sel >= s.filtrados.length) s.sel = 0;
        s.inicio = tk.inicio;

        garantirEstilo();
        if (!s.popup) {
            s.popup = document.createElement('div');
            s.popup.className = 'rr-pop';
            s.popup.addEventListener('mousedown', e => e.preventDefault());   // não tira o foco da caixa
            document.body.appendChild(s.popup);
        }

        let html = `<div class="rr-cab">⚡ Respostas rápidas ${tk.busca ? `· <b>/${escapar(tk.busca)}</b>` : ''}</div>`;
        if (!s.itens.length) {
            html += `<div class="rr-vazio">Nenhuma resposta rápida cadastrada ainda.</div>`;
        } else if (!s.filtrados.length) {
            html += `<div class="rr-vazio">Nenhuma resposta com "${escapar(tk.busca)}".</div>`;
        } else {
            html += '<div class="rr-lista">' + s.filtrados.map((i, n) => `
                <div class="rr-item ${n === s.sel ? 'sel' : ''}" data-n="${n}">
                    <span class="rr-at">/${escapar(i.atalho)}</span>
                    <span class="rr-ti">${escapar(i.titulo)}</span>
                    <span class="rr-tx">${i.qtdImagens ? `🖼️ ${i.qtdImagens} imagem(ns) · ` : ''}${escapar(resolver(semImagens(i.texto), s.ctx))}</span>
                </div>`).join('') + '</div>';
        }
        html += `<div class="rr-rod"><span>↑ ↓ escolher · Enter inserir · Esc fechar</span><a data-gerenciar="1">⚙️ Gerenciar</a></div>`;
        s.popup.innerHTML = html;

        s.popup.querySelectorAll('.rr-item').forEach(d =>
            d.addEventListener('click', () => inserir(el, s.filtrados[+d.dataset.n])));
        const g = s.popup.querySelector('[data-gerenciar]');
        if (g) g.addEventListener('click', () => {
            fechar(el);
            try { Blazor.navigateTo('/respostas-rapidas'); } catch { location.href = '/respostas-rapidas'; }
        });

        posicionar(el, s.popup);
        const selEl = s.popup.querySelector('.rr-item.sel');
        if (selEl) selEl.scrollIntoView({ block: 'nearest' });
    }

    function semImagens(t) { return (t || '').replace(/\[\[img:[A-Za-z0-9_-]+\]\]/g, ' ').replace(/\s{2,}/g, ' ').trim(); }

    function inserir(el, item) {
        const s = estado.get(el);
        const tk = tokenAtual(el);
        if (!s || !item || !tk) { fechar(el); return; }

        // Resposta com imagens: o HELP mostra a sequência (texto, imagem, texto...) para conferir e enviar
        if (item.qtdImagens > 0 && s.ref) {
            el.value = el.value.slice(0, tk.inicio) + el.value.slice(tk.fim);
            el.setSelectionRange(tk.inicio, tk.inicio);
            fechar(el);
            el.dispatchEvent(new Event('input', { bubbles: true }));
            try { s.ref.invokeMethodAsync('AbrirRespostaRapidaComImagens', item.codigo); } catch { }
            return;
        }

        const texto = resolver(item.texto, s.ctx);
        el.value = el.value.slice(0, tk.inicio) + texto + el.value.slice(tk.fim);
        const pos = tk.inicio + texto.length;
        el.focus();
        el.setSelectionRange(pos, pos);
        fechar(el);
        el.dispatchEvent(new Event('input', { bubbles: true }));   // rascunho, maiúscula e tamanho da caixa

        if (s.ref) { try { s.ref.invokeMethodAsync('RegistrarUsoRespostaRapida', item.codigo); } catch { } }
    }

    // ════════════════════════════════════════════════════════════════
    // Editor da tela de cadastro: texto + imagens onde o cursor estiver
    // As imagens viram [[img:chave]] no texto, na posição em que foram coladas.
    // ════════════════════════════════════════════════════════════════
    const editores = {};   // id -> { ref, imgs: {chave: dataUrl}, timer }

    function reduzirImagem(file, max = 1600) {
        return new Promise((ok, erro) => {
            const url = URL.createObjectURL(file);
            const img = new Image();
            img.onload = () => {
                const escala = Math.min(1, max / Math.max(img.width, img.height));
                const c = document.createElement('canvas');
                c.width = Math.round(img.width * escala);
                c.height = Math.round(img.height * escala);
                const g = c.getContext('2d');
                g.fillStyle = '#fff'; g.fillRect(0, 0, c.width, c.height);
                g.drawImage(img, 0, 0, c.width, c.height);
                URL.revokeObjectURL(url);
                ok(c.toDataURL('image/jpeg', 0.85));
            };
            img.onerror = e => { URL.revokeObjectURL(url); erro(e); };
            img.src = url;
        });
    }

    function criarImg(chave, src) {
        const im = document.createElement('img');
        im.src = src;
        im.dataset.chave = chave;
        im.className = 'rre-img';
        im.contentEditable = 'false';
        im.draggable = false;
        return im;
    }

    // HTML do editor -> texto com [[img:chave]]
    function serializar(box) {
        let out = '';
        const andar = (n, bloco) => {
            for (const c of n.childNodes) {
                if (c.nodeType === 3) out += c.nodeValue.replace(/\u00a0/g, ' ');
                else if (c.nodeName === 'BR') out += '\n';
                else if (c.nodeName === 'IMG' && c.dataset.chave) out += `[[img:${c.dataset.chave}]]`;
                else if (c.nodeName === 'DIV' || c.nodeName === 'P') {
                    if (out.length && !out.endsWith('\n')) out += '\n';
                    andar(c, true);
                    if (!out.endsWith('\n')) out += '\n';
                }
                else andar(c, false);
            }
        };
        andar(box, false);
        return out.replace(/\n{3,}/g, '\n\n').replace(/\n+$/, '');
    }

    function render(box, texto, imgs) {
        box.innerHTML = '';
        const partes = (texto || '').split(/(\[\[img:[A-Za-z0-9_-]+\]\])/);
        for (const p of partes) {
            const m = p.match(/^\[\[img:([A-Za-z0-9_-]+)\]\]$/);
            if (m) {
                if (imgs[m[1]]) box.appendChild(criarImg(m[1], imgs[m[1]]));
                continue;
            }
            const linhas = p.split('\n');
            linhas.forEach((l, i) => {
                if (i > 0) box.appendChild(document.createElement('br'));
                if (l) box.appendChild(document.createTextNode(l));
            });
        }
    }

    function noEditor(box) {
        const sel = window.getSelection();
        return sel && sel.rangeCount && box.contains(sel.getRangeAt(0).commonAncestorContainer) ? sel.getRangeAt(0) : null;
    }

    function inserirNo(box, node) {
        let r = noEditor(box);
        if (!r) { box.focus(); r = document.createRange(); r.selectNodeContents(box); r.collapse(false); }
        r.deleteContents();
        r.insertNode(node);
        r.setStartAfter(node); r.collapse(true);
        const sel = window.getSelection(); sel.removeAllRanges(); sel.addRange(r);
    }

    function avisar(id) {
        const ed = editores[id]; const box = document.getElementById(id);
        if (!ed || !box) return;
        clearTimeout(ed.timer);
        ed.timer = setTimeout(() => {
            const t = serializar(box);
            // imagens apagadas do editor saem da lista
            ed.ref.invokeMethodAsync('EditorRespostaMudou', t).catch(() => { });
        }, 180);
    }

    async function colarImagem(id, file) {
        const ed = editores[id]; const box = document.getElementById(id);
        if (!ed || !box || !file || !file.type.startsWith('image/')) return;
        try {
            const dataUrl = await reduzirImagem(file);
            const chave = 'n' + Date.now().toString(36) + Math.random().toString(36).slice(2, 6);
            ed.imgs[chave] = dataUrl;
            await ed.ref.invokeMethodAsync('ImagemColadaRespostaRapida', chave, dataUrl);
            inserirNo(box, criarImg(chave, dataUrl));
            avisar(id);
        } catch (e) { console.warn('[RespostasRapidas] imagem', e); }
    }

    window.rrEditorIniciar = (id, texto, imgs, dotnetRef, inputArquivoId) => {
        const box = document.getElementById(id);
        if (!box) return;
        editores[id] = editores[id] || { imgs: {} };
        const ed = editores[id];
        ed.ref = dotnetRef;
        ed.imgs = Object.assign({}, imgs || {});
        render(box, texto, ed.imgs);

        if (box.dataset.rre !== '1') {
            box.dataset.rre = '1';
            box.addEventListener('input', () => avisar(id));
            box.addEventListener('paste', async e => {
                const itens = e.clipboardData ? Array.from(e.clipboardData.items) : [];
                const imgItens = itens.filter(i => i.type && i.type.startsWith('image/'));
                e.preventDefault();
                if (imgItens.length) {
                    for (const it of imgItens) await colarImagem(id, it.getAsFile());
                    return;
                }
                // texto colado entra sem formatação
                const txt = e.clipboardData ? e.clipboardData.getData('text/plain') : '';
                if (txt) document.execCommand('insertText', false, txt);
            });
            box.addEventListener('drop', async e => {
                const files = e.dataTransfer ? Array.from(e.dataTransfer.files).filter(f => f.type.startsWith('image/')) : [];
                if (!files.length) return;
                e.preventDefault();
                if (document.caretRangeFromPoint) {
                    const r = document.caretRangeFromPoint(e.clientX, e.clientY);
                    if (r) { const sel = window.getSelection(); sel.removeAllRanges(); sel.addRange(r); }
                }
                for (const f of files) await colarImagem(id, f);
            });
            box.addEventListener('keydown', e => {
                if (e.key === 'Enter') { e.preventDefault(); document.execCommand('insertLineBreak'); }
            });
        }

        const inp = inputArquivoId ? document.getElementById(inputArquivoId) : null;
        if (inp && inp.dataset.rre !== '1') {
            inp.dataset.rre = '1';
            inp.addEventListener('change', async () => {
                for (const f of Array.from(inp.files || [])) await colarImagem(id, f);
                inp.value = '';
            });
        }
    };

    // Variável ({cliente}...) entra onde está o cursor
    window.rrEditorInserirTexto = (id, txt) => {
        const box = document.getElementById(id);
        if (!box) return;
        inserirNo(box, document.createTextNode(txt));
        avisar(id);
    };

    // itens: [{ codigo, atalho, titulo, texto, qtdImagens }], ctx: { cliente, atendente }
    window.rrIniciar = (textareaId, itens, ctx, dotnetRef) => {
        const el = document.getElementById(textareaId);
        if (!el) return;

        const anterior = estado.get(el);
        const s = anterior || { popup: null, filtrados: [], sel: 0, inicio: 0 };
        s.itens = (itens || []).map(i => ({ ...i, atalho: (i.atalho || '').toLowerCase() }));
        s.ctx = ctx || {};
        s.ref = dotnetRef;
        estado.set(el, s);
        if (anterior) return;   // eventos já ligados

        el.addEventListener('input', () => { s.sel = 0; desenhar(el); });
        el.addEventListener('click', () => desenhar(el));
        el.addEventListener('blur', () => setTimeout(() => fechar(el), 150));

        // capture: roda antes do Enter que envia a mensagem
        el.addEventListener('keydown', e => {
            if (!s.popup) return;
            if (e.key === 'Escape') { e.preventDefault(); e.stopImmediatePropagation(); fechar(el); return; }
            if (!s.filtrados.length) return;
            if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
                e.preventDefault(); e.stopImmediatePropagation();
                const n = s.filtrados.length;
                s.sel = (s.sel + (e.key === 'ArrowDown' ? 1 : n - 1)) % n;
                desenhar(el);
            } else if ((e.key === 'Enter' && !e.shiftKey) || e.key === 'Tab') {
                e.preventDefault(); e.stopImmediatePropagation();
                inserir(el, s.filtrados[s.sel]);
            }
        }, true);

        window.addEventListener('resize', () => { if (s.popup) posicionar(el, s.popup); });
    };
})();
