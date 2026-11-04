using Microsoft.JSInterop;

namespace HelpMobile.Services
{
    /// <summary>
    /// Login salvo no aparelho. O usuário entra uma vez e só sai se tocar em "Sair".
    /// </summary>
    public class AuthState
    {
        private const string ChaveToken = "help_token";
        private const string ChaveUsuario = "help_usuario";
        private const string ChaveEmpresa = "help_empresa";

        private readonly IJSRuntime _js;

        public AuthState(IJSRuntime js) => _js = js;

        public string Token { get; private set; } = "";
        public string Usuario { get; private set; } = "";
        public string Empresa { get; private set; } = "";
        public bool Logado => !string.IsNullOrWhiteSpace(Token);

        public event Action? Mudou;

        public async Task CarregarAsync()
        {
            Token = await _js.InvokeAsync<string?>("helpMobile.get", ChaveToken) ?? "";
            Usuario = await _js.InvokeAsync<string?>("helpMobile.get", ChaveUsuario) ?? "";
            Empresa = await _js.InvokeAsync<string?>("helpMobile.get", ChaveEmpresa) ?? "";
        }

        public async Task EntrarAsync(string token, string usuario, string empresa)
        {
            Token = token;
            Usuario = usuario;
            Empresa = empresa;
            await _js.InvokeVoidAsync("helpMobile.set", ChaveToken, token);
            await _js.InvokeVoidAsync("helpMobile.set", ChaveUsuario, usuario);
            await _js.InvokeVoidAsync("helpMobile.set", ChaveEmpresa, empresa);
            Mudou?.Invoke();
        }

        public async Task SairAsync()
        {
            Token = "";
            Usuario = "";
            Empresa = "";
            await _js.InvokeVoidAsync("helpMobile.del", ChaveToken);
            await _js.InvokeVoidAsync("helpMobile.del", ChaveUsuario);
            await _js.InvokeVoidAsync("helpMobile.del", ChaveEmpresa);
            Mudou?.Invoke();
        }
    }
}
