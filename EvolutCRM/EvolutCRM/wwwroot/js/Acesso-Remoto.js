window.helpDesktop = {
    abrirAcessoRemoto: function (codigoAcesso) {
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage({
                type: "abrir-acesso-remoto",
                codigoAcesso: codigoAcesso
            });

            return true;
        }

        return false;
    }
};