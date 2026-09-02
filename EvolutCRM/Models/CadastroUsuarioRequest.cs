namespace EvolutCRM.Models
{
    public class CadastroUsuarioRequest
    {
        public string Usuario { get; set; } = "";
        public string Email { get; set; } = "";
        public string Senha { get; set; } = "";
        public string Requer2FA { get; set; } = "S";
        public int CodEmpresa { get; set; }
    }

    public class CadastroUsuarioResult
    {
        public bool Sucesso { get; set; }
        public string Mensagem { get; set; } = "";

        public static CadastroUsuarioResult Ok(string msg = "Usuário cadastrado com sucesso.")
            => new() { Sucesso = true, Mensagem = msg };

        public static CadastroUsuarioResult Erro(string msg)
            => new() { Sucesso = false, Mensagem = msg };
    }

    public class ValidarLoginResult
    {
        public bool Sucesso { get; set; }
        public string Mensagem { get; set; } = "";
        public bool Requer2FA { get; set; }
        public string Email { get; set; } = "";

        public static ValidarLoginResult Ok(bool requer2FA, string email)
            => new() { Sucesso = true, Requer2FA = requer2FA, Email = email };

        public static ValidarLoginResult Erro(string msg)
            => new() { Sucesso = false, Mensagem = msg };
    }
}
