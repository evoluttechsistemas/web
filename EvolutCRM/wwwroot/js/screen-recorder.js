window.evolutScreenRecorder = {
    recorder: null,
    chunks: [],
    stream: null,
    mimeType: "video/webm",
    extensao: "webm",

    obterMelhorFormato: function () {
        const formatos = [
            "video/mp4;codecs=h264,aac",
            "video/mp4",
            "video/webm;codecs=h264,opus",
            "video/webm;codecs=vp9,opus",
            "video/webm;codecs=vp8,opus",
            "video/webm"
        ];

        for (const formato of formatos) {
            if (MediaRecorder.isTypeSupported(formato)) {
                return formato;
            }
        }

        return "";
    },

    iniciar: async function () {
        this.chunks = [];

        const screenStream = await navigator.mediaDevices.getDisplayMedia({
            video: true,
            audio: true
        });

        let micStream = null;

        try {
            micStream = await navigator.mediaDevices.getUserMedia({
                audio: {
                    echoCancellation: true,
                    noiseSuppression: true
                }
            });
        } catch {
            micStream = null;
        }

        const tracks = [
            ...screenStream.getVideoTracks(),
            ...screenStream.getAudioTracks()
        ];

        if (micStream) {
            tracks.push(...micStream.getAudioTracks());
        }

        this.stream = new MediaStream(tracks);

        this.mimeType = this.obterMelhorFormato();

        const options = {
            videoBitsPerSecond: 2500000
        };

        if (this.mimeType) {
            options.mimeType = this.mimeType;
        }

        this.recorder = new MediaRecorder(this.stream, options);

        this.recorder.ondataavailable = e => {
            if (e.data && e.data.size > 0) {
                this.chunks.push(e.data);
            }
        };

        this.recorder.start();

        console.log("Formato de gravação usado:", this.mimeType || "padrão do navegador");

        return true;
    },

    parar: async function () {
        return new Promise((resolve, reject) => {
            if (!this.recorder) {
                reject("Gravação não iniciada.");
                return;
            }

            this.recorder.onstop = async () => {
                const tipoFinal = this.mimeType || this.recorder.mimeType || "video/webm";

                const blob = new Blob(this.chunks, {
                    type: tipoFinal
                });

                const base64 = await this.blobToBase64(blob);

                if (this.stream) {
                    this.stream.getTracks().forEach(t => t.stop());
                }

                const extensao = tipoFinal.includes("mp4")
                    ? "mp4"
                    : "webm";

                const fileName =
                    "gravacao_tela_" +
                    new Date().getTime() +
                    "." +
                    extensao;

                this.recorder = null;
                this.stream = null;
                this.chunks = [];

                resolve({
                    fileName: fileName,
                    mimeType: tipoFinal,
                    base64: base64
                });
            };

            this.recorder.stop();
        });
    },

    cancelar: function () {
        try {
            if (this.recorder && this.recorder.state !== "inactive") {
                this.recorder.stop();
            }

            if (this.stream) {
                this.stream.getTracks().forEach(t => t.stop());
            }
        } catch { }

        this.recorder = null;
        this.stream = null;
        this.chunks = [];
    },

    blobToBase64: function (blob) {
        return new Promise((resolve, reject) => {
            const reader = new FileReader();

            reader.onloadend = () => {
                const result = reader.result;
                const base64 = result.split(",")[1];
                resolve(base64);
            };

            reader.onerror = reject;

            reader.readAsDataURL(blob);
        });
    }
};