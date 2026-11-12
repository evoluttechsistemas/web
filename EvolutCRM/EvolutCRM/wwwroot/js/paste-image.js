window.enablePasteImage = (textareaId, dotnetRef) => {
    console.log("[PasteImage] Inicializando para:", textareaId);

    const el = document.getElementById(textareaId);

    if (!el) {
        console.warn("[PasteImage] Textarea não encontrado:", textareaId);
        return;
    }

    // Guarda quem recebe imagem: o print do HelpDesktop (Ctrl+Alt+P) vai para o ticket visível
    window.__pasteTargets = window.__pasteTargets || {};
    window.__pasteTargets[textareaId] = dotnetRef;

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

// Print do HelpDesktop: entrega a imagem ao campo de mensagem que está na tela (aba ativa)
window.crmColarImagemNoTicket = async (dataUrl) => {
    if (!dataUrl || typeof dataUrl !== "string" || !dataUrl.startsWith("data:image/")) return false;
    const alvos = Object.entries(window.__pasteTargets || {}).reverse();
    for (const [id, ref] of alvos) {
        const el = document.getElementById(id);
        if (!el) { delete window.__pasteTargets[id]; continue; }
        if (el.offsetParent === null || el.disabled) continue;   // aba escondida
        try {
            await ref.invokeMethodAsync("OnImagemColada", dataUrl, "image/png");
            setTimeout(() => el.focus(), 50);
            return true;
        } catch (err) {
            console.warn("[PasteImage] Print não entregue em", id, err);
        }
    }
    return false;
};
