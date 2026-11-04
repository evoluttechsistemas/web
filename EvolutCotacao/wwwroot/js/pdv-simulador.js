export function registerHotkeys(dotNetRef) {
    let pending = false;
    const handler = async (event) => {
        const keys = ["F5", "F8", "F9", "F10"];
        const payment = document.querySelector(".pdv-pagamento");
        const printing = document.querySelector(".pdv-impressao");
        const paymentKey = (payment || printing) && ["Enter", "Escape"].includes(event.key);
        const printKey = printing && event.key === "F6";
        const cardKey = document.querySelector(".pagamento-cartoes") && ["ArrowUp", "ArrowDown"].includes(event.key);

        if (!keys.includes(event.key) && !paymentKey && !cardKey && !printKey) {
            return;
        }

        event.preventDefault();
        event.stopPropagation();
        if (pending || event.repeat) return;
        pending = true;
        try {
            await dotNetRef.invokeMethodAsync("AtalhoPagamento", event.key);
        } finally {
            pending = false;
        }
    };

    window.addEventListener("keydown", handler, true);

    return {
        dispose() {
            window.removeEventListener("keydown", handler, true);
        }
    };
}
export function focusElement(id) {
    const element = document.getElementById(id);
    if (!element) return;
    element.focus();
    if (element instanceof HTMLInputElement) element.select();
}
export function printReceipt() {
    window.print();
}
export function shareReceipt(text) {
    window.open("https://wa.me/?text=" + encodeURIComponent(text), "_blank", "noopener,noreferrer");
}
export async function exitFullscreen() {
    if (document.fullscreenElement) await document.exitFullscreen();
}

export async function toggleFullscreen() {
    if (document.fullscreenElement) await document.exitFullscreen();
    else await document.documentElement.requestFullscreen();
}
