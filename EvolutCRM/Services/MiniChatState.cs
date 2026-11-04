namespace EvolutCRM.Services;

/// <summary>
/// Scoped: qual conversa está aberta na janelinha de chat da faixa da equipe.
/// O MainLayout usa para não mostrar toast de mensagem nova da conversa que já está aberta
/// e para tirar do painel de notificações os avisos dessa conversa quando ela é aberta.
/// </summary>
public class MiniChatState
{
    public int? CodConversaAberta { get; private set; }

    /// <summary>Disparado quando uma conversa é aberta na janelinha.</summary>
    public event Action<int>? OnConversaAberta;

    public void DefinirAberta(int? codConversa)
    {
        CodConversaAberta = codConversa;
        if (codConversa is int cod and > 0)
        {
            try { OnConversaAberta?.Invoke(cod); } catch { }
        }
    }
}
