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