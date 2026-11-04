using EvolutDelivery.Models;
using Microsoft.AspNetCore.SignalR;

namespace EvolutDelivery.Services
{
    // Hub SignalR para notificações em tempo real
    public class NotificacaoHub : Hub
    {
        public async Task EnviarNotificacaoPedido(int codPedido, string mensagem)
        {
            await Clients.All.SendAsync("ReceberNotificacao", codPedido, mensagem);
        }

        public async Task AtualizarStatusPedido(int codPedido, string novoStatus)
        {
            await Clients.All.SendAsync("StatusPedidoAtualizado", codPedido, novoStatus);
        }

        public async Task NotificarNovoPedido(Pedido pedido)
        {
            await Clients.Group("Administradores").SendAsync("NovoPedido", pedido);
        }
    }

    // Serviço de notificações
    public class NotificacaoService
    {
        private readonly IHubContext<NotificacaoHub> _hubContext;

        public NotificacaoService(IHubContext<NotificacaoHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public async Task NotificarNovoPedidoAsync(Pedido pedido)
        {
            await _hubContext.Clients.All.SendAsync("NovoPedido", new
            {
                pedido.Codigo,
                pedido.NomeCliente,
                pedido.ValorTotal,
                pedido.DataPedido
            });
        }

        public async Task NotificarMudancaStatusAsync(int codPedido, string novoStatus, string telefoneCliente)
        {
            await _hubContext.Clients.All.SendAsync("StatusAtualizado", new
            {
                CodPedido = codPedido,
                Status = novoStatus,
                Telefone = telefoneCliente
            });
        }

        public async Task NotificarEntregadorAsync(int codEntregador, string mensagem)
        {
            await _hubContext.Clients.Group($"Entregador-{codEntregador}")
                .SendAsync("NotificacaoEntregador", mensagem);
        }
    }
}
