window.enablePasteImage = (textareaId, dotnetRef) => {
    console.log("[PasteImage] Inicializando para:", textareaId);

    const el = document.getElementById(textareaId);

    if (!el) {
        console.warn("[PasteImage] Textarea não encontrado:", textareaId);
        return;
    }

    el.addEventListener("paste", async (e) => {
        if (!e.clipboardData) return;

        const items = e.clipboardData.items;

        for (const item of items) {
            if (item.type.startsWith("image/")) {
                e.preventDefault();

                const file = item.getAsFile();

                el.style.opacity = "0.5";
                el.disabled = true;

                const reader = new FileReader();

                reader.onload = async () => {
                    try {
                        await dotnetRef.invokeMethodAsync(
                            "OnImagemColada",
                            reader.result,
                            file.type
                        );
                    } catch (err) {
                        console.error("[PasteImage] Erro ao enviar imagem:", err);
                    } finally {
                        el.style.opacity = "1";
                        el.disabled = false;
                        setTimeout(() => el.focus(), 50);
                    }
                };

                reader.onerror = () => {
                    console.error("[PasteImage] Erro ao ler arquivo");
                    el.style.opacity = "1";
                    el.disabled = false;
                };

                reader.readAsDataURL(file);
                return;
            }
        }
    });
};

// ---------------------------------------------------------------------------
// Colar print (Ctrl+V) nos chats: Chat Interno e chat pequeno (MiniChat).
// Fica neste arquivo (carregado no App.razor em todas as páginas) porque um
// <script> dentro de componente Blazor só executa no carregamento inicial da
// página; abrindo o chat pela aba/menu a função não existia e o colar falhava.
// Pode ser chamada a cada render: liga o evento uma vez por campo e só atualiza
// a referência do componente.
// ---------------------------------------------------------------------------
window.ligarColarImagemChat = (idCampo, dotnetRef, metodo) => {
    const el = document.getElementById(idCampo);
    if (!el) return false;

    el._colarImagemRef = dotnetRef;
    el._colarImagemMetodo = metodo;
    if (el.dataset.colarImagemChat === "1") return true;
    el.dataset.colarImagemChat = "1";

    el.addEventListener("paste", (e) => {
        const dados = e.clipboardData;
        if (!dados) return;

        let file = null;
        for (const item of Array.from(dados.items || [])) {
            if (item.kind === "file" && item.type && item.type.startsWith("image/")) {
                file = item.getAsFile();
                if (file) break;
            }
        }
        // Alguns programas colocam a imagem só em "files"
        if (!file) file = Array.from(dados.files || []).find(f => f.type && f.type.startsWith("image/")) || null;
        if (!file) return;   // sem imagem: deixa colar o texto normalmente

        e.preventDefault();
        const reader = new FileReader();
        reader.onload = () => {
            const ref = el._colarImagemRef;
            if (!ref) return;
            ref.invokeMethodAsync(el._colarImagemMetodo, reader.result, file.name || "print.png")
                .catch(err => console.error("[ColarChat] Erro ao enviar a imagem:", err));
        };
        reader.onerror = () => console.error("[ColarChat] Erro ao ler a imagem colada");
        reader.readAsDataURL(file);
    });

    return true;
};
