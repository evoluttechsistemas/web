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
                    <span class="rr-tx">${escapar(resolver(i.texto, s.ctx))}</span>
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

    function inserir(el, item) {
        const s = estado.get(el);
        const tk = tokenAtual(el);
        if (!s || !item || !tk) { fechar(el); return; }

        const texto = resolver(item.texto, s.ctx);
        el.value = el.value.slice(0, tk.inicio) + texto + el.value.slice(tk.fim);
        const pos = tk.inicio + texto.length;
        el.focus();
        el.setSelectionRange(pos, pos);
        fechar(el);
        el.dispatchEvent(new Event('input', { bubbles: true }));   // rascunho, maiúscula e tamanho da caixa

        if (s.ref) { try { s.ref.invokeMethodAsync('RegistrarUsoRespostaRapida', item.codigo); } catch { } }
    }

    // itens: [{ codigo, atalho, titulo, texto }], ctx: { cliente, atendente }
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
