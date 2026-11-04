window.renderGraficosEstatisticas = (dados) => {
    if (!dados) return;

    // Gráfico de Faturamento por forma de pagamento
    const ctx1 = document.getElementById("graficoPagamentos");
    new Chart(ctx1, {
        type: "bar",
        data: {
            labels: ["Dinheiro", "Cartão", "PIX", "Crediário"],
            datasets: [{
                label: "Faturamento (R$)",
                data: [dados.dinheiro, dados.cartao, dados.pix, dados.crediario],
                backgroundColor: ["#4CAF50", "#2196F3", "#FF9800", "#9C27B0"]
            }]
        },
        options: {
            responsive: true,
            plugins: { legend: { display: false } },
            scales: { y: { beginAtZero: true } }
        }
    });

    // Gráfico de Evolução (simulação simples)
    const ctx2 = document.getElementById("graficoEvolucao");
    new Chart(ctx2, {
        type: "line",
        data: {
            labels: ["01", "05", "10", "15", "20", "25", "30"],
            datasets: [{
                label: "Faturamento Diário (R$)",
                data: [1200, 1500, 800, 2000, 2300, 1900, dados.faturamento],
                borderColor: "#0078D7",
                tension: 0.3,
                fill: false
            }]
        },
        options: { responsive: true, plugins: { legend: { display: false } } }
    });
};
