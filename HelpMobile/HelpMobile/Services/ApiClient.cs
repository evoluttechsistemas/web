using HelpMobile.Models;
using Microsoft.AspNetCore.Components;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace HelpMobile.Services
{
    /// <summary>Erro de API com mensagem pronta para mostrar ao usuário.</summary>
    public class ApiException : Exception
    {
        public ApiException(string mensagem) : base(mensagem) { }
    }

    /// <summary>
    /// Todas as chamadas para o HELP (/api/mobile/...). O app não tem banco: tudo passa por aqui.
    /// </summary>
    public class ApiClient
    {
        private readonly HttpClient _http;
        private readonly AuthState _auth;
        private readonly NavigationManager _nav;

        public ApiClient(HttpClient http, AuthState auth, NavigationManager nav)
        {
            _http = http;
            _auth = auth;
            _nav = nav;
        }

        /// <summary>Última lista carregada: ao voltar de um ticket a lista aparece na hora.</summary>
        public List<MobileTicketItemDto>? UltimasConversas { get; set; }

        public string BaseUrl => _http.BaseAddress?.ToString().TrimEnd('/') ?? "";

        /// <summary>Monta o endereço completo de imagem/áudio/vídeo que vem do HELP como "/api/...".</summary>
        public string? Url(string? caminho)
        {
            if (string.IsNullOrWhiteSpace(caminho)) return null;
            if (caminho.StartsWith("http", StringComparison.OrdinalIgnoreCase) || caminho.StartsWith("data:")) return caminho;
            return BaseUrl + (caminho.StartsWith('/') ? caminho : "/" + caminho);
        }

        // ───────────────────────── Login ─────────────────────────

        public async Task<MobileLoginResponse> LoginAsync(string usuario, string senha)
        {
            HttpResponseMessage resp;
            try
            {
                resp = await _http.PostAsJsonAsync("api/mobile/login", new MobileLoginRequest { Usuario = usuario, Senha = senha });
            }
            catch (HttpRequestException)
            {
                throw new ApiException("Sem conexão com o HELP. Verifique a internet.");
            }

            if (!resp.IsSuccessStatusCode)
                throw new ApiException(await LerMensagemAsync(resp, "Não foi possível entrar."));

            return (await resp.Content.ReadFromJsonAsync<MobileLoginResponse>())!;
        }

        // ───────────────────────── Tickets ─────────────────────────

        public Task<MobileEuDto?> EuAsync() => GetAsync<MobileEuDto>("api/mobile/eu");

        /// <summary>Equipe (online, status e atendimentos) para a faixa do topo.</summary>
        public async Task<List<MobileEquipeMembroDto>> EquipeAsync() =>
            await GetAsync<List<MobileEquipeMembroDto>>("api/mobile/equipe") ?? new();

        /// <param name="todos">false = meus + fila (padrão); true = tickets em aberto de todos os usuários.</param>
        public async Task<List<MobileTicketItemDto>> ConversasAsync(bool todos = false) =>
            await GetAsync<List<MobileTicketItemDto>>(todos ? "api/mobile/tickets?todos=true" : "api/mobile/tickets") ?? new();

        public Task<MobileTicketDto?> TicketAsync(int id) => GetAsync<MobileTicketDto>($"api/mobile/tickets/{id}");

        public async Task<List<MobileMensagemDto>> MensagensAsync(int id) =>
            await GetAsync<List<MobileMensagemDto>>($"api/mobile/tickets/{id}/mensagens") ?? new();

        /// <summary>Abre a conversa numa chamada só: cabeçalho + últimas mensagens (e já marca como lido).</summary>
        public Task<MobileAbrirDto?> AbrirAsync(int id, int limite) =>
            GetAsync<MobileAbrirDto>($"api/mobile/tickets/{id}/abrir?limite={limite}");

        /// <summary>Mensagens anteriores a <paramref name="antes"/> (ao rolar para cima).</summary>
        public async Task<List<MobileMensagemDto>> MensagensAnterioresAsync(int id, int antes, int limite) =>
            await GetAsync<List<MobileMensagemDto>>($"api/mobile/tickets/{id}/mensagens?antes={antes}&limite={limite}") ?? new();

        /// <param name="statusDesde">Status de entrega só a partir desta mensagem (a mais antiga carregada).</param>
        public Task<MobileAtualizacaoDto?> AtualizacoesAsync(int id, int depois, int statusDesde) =>
            GetAsync<MobileAtualizacaoDto>($"api/mobile/tickets/{id}/atualizacoes?depois={depois}&statusDesde={statusDesde}");

        public async Task<List<string>> AtendentesAsync() =>
            await GetAsync<List<string>>("api/mobile/usuarios") ?? new();

        /// <param name="gerarInsight">Escolha do atendente: gerar ou não o insight (caso aprendido) do ticket.</param>
        public async Task<string> FinalizarAsync(int id, bool gerarInsight) =>
            (await PostJsonAsync<MobileEnviarResponse>(
                $"api/mobile/tickets/{id}/finalizar?insight={(gerarInsight ? "true" : "false")}", new { }))?.Mensagem ?? "";

        public async Task<string> TransferirAsync(int id, string usuario) =>
            (await PostJsonAsync<MobileEnviarResponse>($"api/mobile/tickets/{id}/transferir",
                new MobileTransferirRequest { Usuario = usuario }))?.Mensagem ?? "";

        public async Task<List<MobileClienteDto>> BuscarClientesAsync(string termo) =>
    await GetAsync<List<MobileClienteDto>>($"api/mobile/clientes?busca={Uri.EscapeDataString(termo ?? "")}") ?? new();

        public async Task<string> VincularClienteAsync(int id, int codCliente) =>
            (await PostJsonAsync<MobileEnviarResponse>($"api/mobile/tickets/{id}/cliente",
                new MobileVincularClienteRequest { CodCliente = codCliente }))?.Mensagem ?? "";

        // ── Novo ticket ──
        public async Task<MobileBuscaNovoTicketDto> NovoTicketBuscarAsync(string termo) =>
            await GetAsync<MobileBuscaNovoTicketDto>($"api/mobile/novo-ticket/clientes?busca={Uri.EscapeDataString(termo ?? "")}") ?? new();

        public async Task<MobileContatosDto> NovoTicketContatosAsync(int codCliente, string termo) =>
            await GetAsync<MobileContatosDto>($"api/mobile/novo-ticket/clientes/{codCliente}/contatos?busca={Uri.EscapeDataString(termo ?? "")}") ?? new();

        public async Task<MobileNovoTicketResponse> CriarTicketAsync(MobileNovoTicketRequest req) =>
            await PostJsonAsync<MobileNovoTicketResponse>("api/mobile/tickets", req) ?? new();

        public Task MarcarLidoAsync(int id) => PostAsync($"api/mobile/tickets/{id}/lido", new { });

        public Task EnviarAsync(int id, MobileEnviarRequest req) => PostAsync($"api/mobile/tickets/{id}/mensagens", req);

        public async Task EnviarArquivoAsync(int id, Stream conteudo, string nome, string contentType, string? legenda)
        {
            using var form = new MultipartFormDataContent();
            var arquivo = new StreamContent(conteudo);
            arquivo.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
            form.Add(arquivo, "arquivo", nome);
            if (!string.IsNullOrWhiteSpace(legenda))
                form.Add(new StringContent(legenda), "legenda");

            using var req = new HttpRequestMessage(HttpMethod.Post, $"api/mobile/tickets/{id}/arquivo") { Content = form };
            await EnviarRequisicaoAsync(req);
        }

        public async Task EnviarAudioAsync(int id, byte[] audio, string contentType, bool interna)
        {
            using var form = new MultipartFormDataContent();
            var conteudo = new ByteArrayContent(audio);
            conteudo.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "audio/webm" : contentType.Split(';')[0]);
            form.Add(conteudo, "audio", "audio.ogg");
            form.Add(new StringContent(interna ? "true" : "false"), "interna");

            using var req = new HttpRequestMessage(HttpMethod.Post, $"api/mobile/tickets/{id}/audio") { Content = form };
            using var _ = await EnviarRequisicaoAsync(req);
        }

        // ───────────────────────── Financeiro ─────────────────────────

        public async Task<List<FinClienteDto>> FinBuscarClientesAsync(string busca) =>
            await GetAsync<List<FinClienteDto>>($"api/mobile/financeiro/clientes?busca={Uri.EscapeDataString(busca)}") ?? new();

        public async Task<List<FinContaDto>> FinContasAsync(int codCliente, string situacao) =>
            await GetAsync<List<FinContaDto>>($"api/mobile/financeiro/clientes/{codCliente}/contas?situacao={situacao}") ?? new();

        public async Task<List<FinContaBancariaDto>> FinContasBancariasAsync() =>
            await GetAsync<List<FinContaBancariaDto>>("api/mobile/financeiro/contas-bancarias") ?? new();

        public async Task<FinResultadoDto> FinBaixaAsync(FinBaixaRequest req) =>
            await PostJsonAsync<FinResultadoDto>("api/mobile/financeiro/baixa", req) ?? new FinResultadoDto();

        public async Task<List<FinCobrancaClienteDto>> FinCobrancaAsync() =>
            await GetAsync<List<FinCobrancaClienteDto>>("api/mobile/financeiro/cobranca") ?? new();

        public Task<FinCobrancaDetalheDto?> FinCobrancaDetalheAsync(int codCliente) =>
            GetAsync<FinCobrancaDetalheDto>($"api/mobile/financeiro/cobranca/{codCliente}");

        public Task FinRegistrarWhatsAppAsync(int codCliente, string tipo) =>
            PostAsync($"api/mobile/financeiro/cobranca/{codCliente}/log", new FinLogRequest { Tipo = tipo });

        public async Task<FinPixDto> FinGerarPixAsync(int codCliente) =>
            await PostJsonAsync<FinPixDto>($"api/mobile/financeiro/cobranca/{codCliente}/pix", new { }) ?? new FinPixDto();

        // ───────────────────────── Push ─────────────────────────

        public async Task<string> ChavePushAsync()
        {
            var r = await _http.GetFromJsonAsync<ChaveDto>("api/mobile/push/chave");
            return r?.Chave ?? "";
        }

        public Task InscreverPushAsync(MobilePushInscricaoRequest req) => PostAsync("api/mobile/push/inscrever", req);

        public Task CancelarPushAsync(string endpoint) =>
            PostAsync("api/mobile/push/cancelar", new MobilePushCancelarRequest { Endpoint = endpoint });

        private class ChaveDto { public string Chave { get; set; } = ""; }

        // ───────────────────────── Base ─────────────────────────

        private async Task<T?> GetAsync<T>(string url)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await EnviarRequisicaoAsync(req);
            if (resp.StatusCode == HttpStatusCode.NotFound) return default;
            return await resp.Content.ReadFromJsonAsync<T>();
        }

        private async Task<T?> PostJsonAsync<T>(string url, object corpo)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(corpo) };
            using var resp = await EnviarRequisicaoAsync(req);
            return await resp.Content.ReadFromJsonAsync<T>();
        }

        private async Task PostAsync(string url, object corpo)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(corpo) };
            using var _ = await EnviarRequisicaoAsync(req);
        }

        private async Task<HttpResponseMessage> EnviarRequisicaoAsync(HttpRequestMessage req)
        {
            if (_auth.Logado)
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _auth.Token);

            HttpResponseMessage resp;
            try
            {
                resp = await _http.SendAsync(req);
            }
            catch (HttpRequestException)
            {
                throw new ApiException("Sem conexão com o HELP.");
            }

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Token vencido ou usuário inativado: volta para o login
                UltimasConversas = null;
                await _auth.SairAsync();
                _nav.NavigateTo("login", replace: true);
                throw new ApiException("Sua sessão terminou. Entre novamente.");
            }

            if (!resp.IsSuccessStatusCode && resp.StatusCode != HttpStatusCode.NotFound)
            {
                var msg = await LerMensagemAsync(resp, "Não foi possível concluir. Tente de novo.");
                resp.Dispose();
                throw new ApiException(msg);
            }

            return resp;
        }

        private static async Task<string> LerMensagemAsync(HttpResponseMessage resp, string padrao)
        {
            try
            {
                var r = await resp.Content.ReadFromJsonAsync<MensagemErro>();
                if (!string.IsNullOrWhiteSpace(r?.Mensagem)) return r!.Mensagem!;
            }
            catch { }
            return padrao;
        }

        private class MensagemErro { public string? Mensagem { get; set; } }
    }
}
