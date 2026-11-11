// Recebe o PDF do circuito autenticado do Blazor. Não publica arquivo nem URL no servidor.
const urls = new Set();

export async function criarPdfUrl(streamReference) {
    const buffer = await streamReference.arrayBuffer();
    const url = URL.createObjectURL(new Blob([buffer], { type: 'application/pdf' }));
    urls.add(url);
    return url;
}

export function liberarPdfUrl(url) {
    URL.revokeObjectURL(url);
    urls.delete(url);
}

export async function baixarPdf(streamReference, filename) {
    const url = await criarPdfUrl(streamReference);
    const link = document.createElement('a');
    link.href = url;
    link.download = filename;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => liberarPdfUrl(url), 60000);
}

export function imprimirPdf(iframeId) {
    const frame = document.getElementById(iframeId);
    if (!frame?.contentWindow) throw new Error('Abra o PDF antes de imprimir.');
    frame.contentWindow.focus();
    frame.contentWindow.print();
}

window.addEventListener('pagehide', () => {
    for (const url of urls) URL.revokeObjectURL(url);
    urls.clear();
});
