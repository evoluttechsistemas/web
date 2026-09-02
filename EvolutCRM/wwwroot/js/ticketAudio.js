window.ticketAudio = {

    recorder: null,
    chunks: [],

    async start() {
        const stream = await navigator.mediaDevices.getUserMedia({ audio: true });

        this.chunks = [];

        // Escolhe o melhor formato suportado pelo navegador
        // O WhatsApp aceita: audio/ogg;codecs=opus (Firefox)
        // ou audio/webm;codecs=opus (Chrome) — ambos funcionam no mobile
        const mimePreferido = [
            'audio/ogg;codecs=opus',
            'audio/ogg',
            'audio/webm;codecs=opus',
            'audio/webm'
        ].find(m => MediaRecorder.isTypeSupported(m)) || '';

        const options = mimePreferido ? { mimeType: mimePreferido } : {};

        this.recorder = new MediaRecorder(stream, options);

        this.recorder.ondataavailable = e => {
            if (e.data.size > 0)
                this.chunks.push(e.data);
        };

        this.recorder.start();
    },

    stop() {
        return new Promise(resolve => {
            this.recorder.onstop = async () => {

                // Usa o mimeType real que o recorder escolheu
                const mimeType = this.recorder.mimeType || 'audio/ogg';

                const blob = new Blob(this.chunks, { type: mimeType });

                const buffer = await blob.arrayBuffer();

                let binary = '';
                const bytes = new Uint8Array(buffer);
                bytes.forEach(b => binary += String.fromCharCode(b));

                // Para o stream (libera o microfone)
                this.recorder.stream?.getTracks().forEach(t => t.stop());

                resolve({
                    base64: btoa(binary),
                    mimeType: mimeType
                });
            };

            this.recorder.stop();
        });
    }
};