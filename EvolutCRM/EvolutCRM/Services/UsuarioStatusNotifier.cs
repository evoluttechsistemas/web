namespace EvolutCRM.Services;

/// <summary>
/// Singleton: avisa todas as telas abertas (de todos os usuários) que o status
/// de alguém da empresa mudou, para a barra da equipe atualizar na hora.
/// </summary>
public class UsuarioStatusNotifier
{
    public event Action<int>? OnMudanca;

    public void Notificar(int codEmp)
    {
        var handlers = OnMudanca;
        if (handlers == null) return;

        foreach (Action<int> h in handlers.GetInvocationList())
        {
            try { h(codEmp); }
            catch { /* uma tela com problema não pode impedir as outras de atualizar */ }
        }
    }
}
