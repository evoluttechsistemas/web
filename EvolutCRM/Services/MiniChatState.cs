namespace EvolutCRM.Services;

/// <summary>
/// Scoped: qual conversa está aberta na janelinha de chat da faixa da equipe.
/// O MainLayout usa para não mostrar toast de mensagem nova da conversa que já está aberta.
/// </summary>
public class MiniChatState
{
    public int? CodConversaAberta { get; private set; }

    public void DefinirAberta(int? codConversa) => CodConversaAberta = codConversa;
}
