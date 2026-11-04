// Assistente IA do atendente (painel do ticket)
// - Ctrl+V de print, arrastar imagem para o painel
// - Enter envia, Shift+Enter quebra linha
// - imagens reduzidas no navegador antes de ir para o servidor (máx. 1600px, JPEG)
(function () {
    const instancias = {};
    const MAX_LADO = 1600;

    function reduzir(file) {
        return new Promise((resolve, reject) => {
            const reader = new FileReader();
            reader.onerror = () => reject(reader.error);
            reader.onload = () => {
                const img = new Image();
                img.onerror = () => reject(new Error("imagem inválida"));
                img.onload = () => {
                    let { width: w, height: h } = img;
                    const escala = Math.min(1, MAX_LADO / Math.max(w, h));
                    w = Math.round(w * escala); h = Math.round(h * escala);
                    const canvas = document.createElement("canvas");
                    canvas.width = w; canvas.height = h;
                    const ctx = canvas.getContext("2d");
                    ctx.fillStyle = "#fff";           // PNG com fundo transparente fica legível
                    ctx.fillRect(0, 0, w, h);
                    ctx.drawImage(img, 0, 0, w, h);
                    resolve(canvas.toDataURL("image/jpeg", 0.85));
                };
                img.src = reader.result;
            };
            reader.readAsDataURL(file);
        });
    }

    async function enviarArquivos(inst, files) {
        for (const f of files) {
            if (!f || !f.type || !f.type.startsWith("image/")) continue;
            try {
                const dataUrl = await reduzir(f);
                await inst.ref.invokeMethodAsync("AiaImagemRecebida", dataUrl);
            } catch (e) {
                console.error("[AssistenteIA] imagem:", e);
            }
        }
    }

    window.assistenteIA = {
        init: function (idTexto, idArea, dotnetRef) {
            window.assistenteIA.dispose(idTexto);
            const txt = document.getElementById(idTexto);
            const area = document.getElementById(idArea);
            if (!txt || !area) return;

            const inst = { ref: dotnetRef, txt, area };

            inst.onPaste = (e) => {
                const itens = e.clipboardData ? Array.from(e.clipboardData.items || []) : [];
                const imgs = itens.filter(i => i.type && i.type.startsWith("image/")).map(i => i.getAsFile());
                if (imgs.length === 0) return;
                e.preventDefault();
                enviarArquivos(inst, imgs);
            };

            inst.onKey = (e) => {
                if (e.key === "Enter" && !e.shiftKey && !e.ctrlKey && !e.altKey && !e.isComposing) {
                    e.preventDefault();
                    // garante que o valor chegou no Blazor antes de enviar
                    txt.dispatchEvent(new Event("input", { bubbles: true }));
                    setTimeout(() => inst.ref.invokeMethodAsync("AiaEnterPressionado").catch(() => { }), 0);
                }
            };

            inst.onInput = () => {
                txt.style.height = "auto";
                txt.style.height = Math.min(txt.scrollHeight, 180) + "px";
            };

            inst.onDragOver = (e) => {
                if (e.dataTransfer && Array.from(e.dataTransfer.types || []).includes("Files")) {
                    e.preventDefault();
                    area.classList.add("aia-arrastando");
                }
            };
            inst.onDragLeave = (e) => {
                if (!area.contains(e.relatedTarget)) area.classList.remove("aia-arrastando");
            };
            inst.onDrop = (e) => {
                area.classList.remove("aia-arrastando");
                if (!e.dataTransfer || !e.dataTransfer.files || e.dataTransfer.files.length === 0) return;
                e.preventDefault();
                enviarArquivos(inst, Array.from(e.dataTransfer.files));
            };

            txt.addEventListener("keydown", inst.onKey);
            txt.addEventListener("input", inst.onInput);
            area.addEventListener("paste", inst.onPaste);   // pega também o Ctrl+V dentro da caixa de texto
            area.addEventListener("dragover", inst.onDragOver);
            area.addEventListener("dragleave", inst.onDragLeave);
            area.addEventListener("drop", inst.onDrop);
            instancias[idTexto] = inst;
        },

        dispose: function (idTexto) {
            const inst = instancias[idTexto];
            if (!inst) return;
            inst.txt.removeEventListener("keydown", inst.onKey);
            inst.txt.removeEventListener("input", inst.onInput);
            inst.area.removeEventListener("paste", inst.onPaste);
            inst.area.removeEventListener("dragover", inst.onDragOver);
            inst.area.removeEventListener("dragleave", inst.onDragLeave);
            inst.area.removeEventListener("drop", inst.onDrop);
            delete instancias[idTexto];
        },

        // rola até o fim só se o atendente já estava perto do fim (não atrapalha quem está lendo acima)
        rolarFim: function (idLista, forcar) {
            const el = document.getElementById(idLista);
            if (!el) return;
            const perto = el.scrollHeight - el.scrollTop - el.clientHeight < 160;
            if (forcar || perto) el.scrollTop = el.scrollHeight;
        },

        focar: function (id) {
            const el = document.getElementById(id);
            if (el) { el.focus(); const v = el.value || ""; el.setSelectionRange(v.length, v.length); }
        },

        ajustarAltura: function (id) {
            const el = document.getElementById(id);
            if (!el) return;
            el.style.height = "auto";
            el.style.height = Math.min(el.scrollHeight, 180) + "px";
        },

        // modelo e esforço escolhidos no painel ficam salvos neste navegador
        lerPreferencias: function () {
            try {
                return [localStorage.getItem("aia.modelo") || "", localStorage.getItem("aia.esforco") || ""];
            } catch { return ["", ""]; }
        },

        salvarPreferencias: function (modelo, esforco) {
            try {
                localStorage.setItem("aia.modelo", modelo || "");
                localStorage.setItem("aia.esforco", esforco || "");
            } catch { }
        },

        copiar: async function (texto) {
            try {
                await navigator.clipboard.writeText(texto);
                return true;
            } catch {
                const t = document.createElement("textarea");
                t.value = texto; t.style.position = "fixed"; t.style.opacity = "0";
                document.body.appendChild(t); t.select();
                let ok = false;
                try { ok = document.execCommand("copy"); } catch { }
                document.body.removeChild(t);
                return ok;
            }
        }
    };
})();
