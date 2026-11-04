window.ticketAudio = {

    recorder: null,
    chunks: [],

    async start() {
        const stream = await navigator.mediaDevices.getUserMedia({ audio: true });

        this.chunks = [];

        const mimePreferido = [
            'audio/webm;codecs=opus',
            'audio/webm',
            'audio/ogg;codecs=opus',
            'audio/ogg'
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

                const mimeType = this.recorder.mimeType || 'audio/webm';

                const blob = new Blob(this.chunks, { type: mimeType });

                const buffer = await blob.arrayBuffer();

                let binary = '';
                const bytes = new Uint8Array(buffer);
                bytes.forEach(b => binary += String.fromCharCode(b));

                // Para o stream (libera o microfone)
                this.recorder.stream?.getTracks().forEach(t => t.stop());

                resolve({
                    base64: btoa(binary),
                    mimeType: 'audio/ogg; codecs=opus'  // sempre reporta OGG/Opus para o banco
                });
            };

            this.recorder.stop();
        });
    }
};