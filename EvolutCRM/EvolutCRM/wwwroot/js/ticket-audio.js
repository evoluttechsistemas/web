window.velocidadeAudioAtual = 1;

window.carregarVelocidadeAudio = (key) => {
    const valor = localStorage.getItem(key);
    const rate = parseFloat(valor);

    if (rate === 1 || rate === 1.5 || rate === 2) {
        window.velocidadeAudioAtual = rate;
        return rate;
    }

    window.velocidadeAudioAtual = 1;
    return 1;
};

window.setVelocidadeTodosAudios = (rate, key) => {
    window.velocidadeAudioAtual = rate;

    if (key) {
        localStorage.setItem(key, rate.toString());
    }

    document.querySelectorAll("audio").forEach(audio => {
        audio.playbackRate = rate;
        audio.defaultPlaybackRate = rate;
    });
};

window.aplicarVelocidadeAudio = (audio) => {
    if (audio) {
        const rate = window.velocidadeAudioAtual || 1;
        audio.playbackRate = rate;
        audio.defaultPlaybackRate = rate;
    }
};