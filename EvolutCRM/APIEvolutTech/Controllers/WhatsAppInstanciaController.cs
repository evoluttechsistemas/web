using EvolutCRM.Models;
using EvolutCRM.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EvolutCRM.APIEvolutTech.Controllers
{
    [ApiController]
    [Route("api/whatsapp")]
    public class WhatsAppFotoController : ControllerBase
    {
        private readonly ClienteWhatsAppFotoService _fotoService;

        public WhatsAppFotoController(ClienteWhatsAppFotoService fotoService)
        {
            _fotoService = fotoService;
        }

        [HttpPost("cliente-foto")]
        public async Task<IActionResult> SalvarFotoCliente([FromBody] WhatsAppFotoClienteRequest req)
        {
            if (req.CodEmp <= 0)
                return BadRequest("CodEmp invalido.");

            if (string.IsNullOrWhiteSpace(req.Telefone))
                return BadRequest("Telefone invalido.");

            if (string.IsNullOrWhiteSpace(req.FotoBase64))
                return BadRequest("FotoBase64 invalida.");

            var url = await _fotoService.SalvarFotoAsync(
                req.CodEmp,
                req.Telefone,
                req.Jid,
                req.FotoBase64,
                req.ContentType
            );

            return Ok(new
            {
                sucesso = true,
                fotoUrl = url
            });
        }

        [HttpGet("cliente-foto/precisa-atualizar")]
        public async Task<IActionResult> PrecisaAtualizarFoto([FromQuery] int codEmp, [FromQuery] string telefone)
        {
            var precisa = await _fotoService.PrecisaAtualizarFotoAsync(codEmp, telefone);

            return Ok(new
            {
                precisaAtualizar = precisa
            });
        }

        [HttpGet("cliente-foto/arquivo")]
        public async Task<IActionResult> ObterArquivo([FromQuery] int codEmp, [FromQuery] string telefone)
        {
            var arquivo = await _fotoService.ObterArquivoAsync(codEmp, telefone);

            if (arquivo is null)
                return NotFound();

            Response.Headers.CacheControl = "public,max-age=86400";
            return File(arquivo.Value.Bytes, arquivo.Value.ContentType);
        }
    }

    [ApiController]
    [Route("api/whatsapp-instancia")]
    public class WhatsAppInstanciaController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly ILogger<WhatsAppInstanciaController> _logger;
        private readonly HttpClient _httpClient;
        private readonly HealthMonitorService _monitor;
        private readonly UserState _userState;
        private readonly TicketService _ticketService;

        private const string BAILEYS_BASE = "http://localhost:3333";

        public WhatsAppInstanciaController(
    IConfiguration config,
    ILogger<WhatsAppInstanciaController> logger,
    HealthMonitorService monitor,
    UserState userState,
    TicketService ticketService)  // ← ADD
        {
            _config = config;
            _logger = logger;
            _monitor = monitor;  // ← ADD
            _userState = userState;
            _ticketService = ticketService;
            _httpClient = new HttpClient();
        }

        private SqlConnection GetConn() =>
            new SqlConnection(_config.GetConnectionString("Connection"));

        private int CodEmpAtual =>
            _userState.CurrentCompanyId > 0 ? _userState.CurrentCompanyId : 2;

        private void AddCodEmp(SqlCommand cmd)
        {
            if (!cmd.Parameters.Contains("@CodEmp"))
                cmd.Parameters.AddWithValue("@CodEmp", CodEmpAtual);
        }

        private static void AddCodEmpOpcional(SqlCommand cmd, int codEmp)
        {
            if (!cmd.Parameters.Contains("@CodEmp"))
                cmd.Parameters.AddWithValue("@CodEmp", codEmp);
        }

        [HttpGet("listar")]
        public async Task<IActionResult> Listar([FromQuery] int codEmp = 0)
        {
            try
            {
                var lista = new List<object>();

                using var conn = GetConn();
                await conn.OpenAsync();

                using var cmd = new SqlCommand(@"
SELECT
    Codigo, Nome, PastaAuth, Status,
    Numero, DataCriacao, DataConexao, DataUltimoPing, QrCodeBase64, CodEmp
FROM WhatsAppInstancia
WHERE CodEmp = @CodEmp
ORDER BY Codigo ASC", conn);
                AddCodEmpOpcional(cmd, ResolverCodEmp(codEmp));

                using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    lista.Add(new
                    {
                        Codigo = rd.GetInt32(0),
                        Nome = rd.IsDBNull(1) ? "" : rd.GetString(1),
                        PastaAuth = rd.IsDBNull(2) ? "" : rd.GetString(2),
                        Status = rd.IsDBNull(3) ? "desconectado" : rd.GetString(3),
                        Numero = rd.IsDBNull(4) ? "" : rd.GetString(4),
                        DataCriacao = rd.IsDBNull(5) ? (DateTime?)null : rd.GetDateTime(5),
                        DataConexao = rd.IsDBNull(6) ? (DateTime?)null : rd.GetDateTime(6),
                        DataUltimoPing = rd.IsDBNull(7) ? (DateTime?)null : rd.GetDateTime(7),
                        QrCodeBase64 = rd.IsDBNull(8) ? null : rd.GetString(8),
                        CodEmp = rd.IsDBNull(9) ? 0 : rd.GetInt32(9)
                    });
                }

                return Ok(lista);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao listar instâncias");
                return StatusCode(500, new { erro = ex.Message });
            }
        }

        // =========================================================
        // CRIAR NOVA INSTÂNCIA
        // Chamado pela tela Blazor ao clicar em "Nova Conexão"
        // =========================================================
        [HttpPost("criar")]
        public async Task<IActionResult> Criar([FromBody] CriarInstanciaDto dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.Nome))
                    return BadRequest(new { erro = "Nome é obrigatório" });

                // Gera nome de pasta automático baseado no nome (sem espaços/acentos)
                var pastaAuth = "auth_" + dto.Nome
                    .ToLower()
                    .Replace(" ", "_")
                    .Replace("ã", "a").Replace("ç", "c").Replace("é", "e")
                    .Replace("ê", "e").Replace("ô", "o").Replace("õ", "o")
                    + "_" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                using var conn = GetConn();
                await conn.OpenAsync();

                // Verifica se já existe instância com o mesmo nome
                var codEmp = ResolverCodEmp(dto.CodEmp);

                using (var cmdVerifica = new SqlCommand(
                    "SELECT COUNT(1) FROM WhatsAppInstancia WHERE Nome = @Nome AND CodEmp = @CodEmp", conn))
                {
                    AddCodEmpOpcional(cmdVerifica, codEmp);
                    cmdVerifica.Parameters.AddWithValue("@Nome", dto.Nome.Trim());
                    int existe = Convert.ToInt32(await cmdVerifica.ExecuteScalarAsync());
                    if (existe > 0)
                        return BadRequest(new { erro = $"Já existe uma instância com o nome '{dto.Nome}'" });
                }

                using var cmdInsere = new SqlCommand(@"
INSERT INTO WhatsAppInstancia (Nome, PastaAuth, Status, DataCriacao, CodEmp)
OUTPUT INSERTED.Codigo
VALUES (@Nome, @PastaAuth, 'desconectado', GETDATE(), @CodEmp)", conn);

                AddCodEmpOpcional(cmdInsere, codEmp);
                cmdInsere.Parameters.AddWithValue("@Nome", dto.Nome.Trim());
                cmdInsere.Parameters.AddWithValue("@PastaAuth", pastaAuth);

                int novoCodigo = Convert.ToInt32(await cmdInsere.ExecuteScalarAsync());

                return Ok(new { sucesso = true, codigo = novoCodigo, pastaAuth });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao criar instância");
                return StatusCode(500, new { erro = ex.Message });
            }
        }

        // =========================================================
        // CONECTAR INSTÂNCIA
        // Chama o Node para iniciar o Baileys e gerar o QR
        // =========================================================
        [HttpPost("conectar")]
        public async Task<IActionResult> Conectar([FromBody] CodigoDto dto)
        {
            try
            {
                InstanciaInfo? inst = await BuscarInstanciaAsync(dto.Codigo, dto.CodEmp);
                if (inst == null)
                    return NotFound(new { erro = "Instância não encontrada" });

                var payload = new
                {
                    codigo = inst.Codigo,
                    pastaAuth = inst.PastaAuth,
                    nome = inst.Nome,
                    codEmp = inst.CodEmp
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var resp = await _httpClient.PostAsync(BAILEYS_BASE + "/instancia/conectar", content);

                if (!resp.IsSuccessStatusCode)
                {
                    var erro = await resp.Content.ReadAsStringAsync();
                    _monitor.Log(LogCategory.Baileys, LogSeverity.Error,                        // ← ADD
                        $"Falha ao conectar instância #{dto.Codigo} ({inst.Nome})",             // ← ADD
                        $"Baileys retornou: {(erro.Length > 150 ? erro[..150] : erro)}");       // ← ADD
                    return StatusCode(500, new { erro = $"Baileys retornou erro: {erro}" });
                }

                await AtualizarStatusBancoAsync(dto.Codigo, "aguardando_qr", null, null, inst.CodEmp);

                _monitor.LogBaileysStatus($"Instância #{dto.Codigo} ({inst.Nome}) aguardando QR", connected: false);  // ← ADD

                return Ok(new { sucesso = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao conectar instância {Codigo}", dto.Codigo);
                _monitor.LogException(LogCategory.Baileys, ex,                          // ← ADD
                    $"Conectar instância #{dto.Codigo}");                               // ← ADD
                return StatusCode(500, new { erro = ex.Message });
            }
        }

        // =========================================================
        // DESCONECTAR INSTÂNCIA
        // =========================================================
        [HttpPost("desconectar")]
        public async Task<IActionResult> Desconectar([FromBody] CodigoDto dto)
        {
            try
            {
                var inst = await BuscarInstanciaAsync(dto.Codigo, dto.CodEmp);
                if (inst == null)
                    return NotFound(new { erro = "Instância não encontrada" });

                var payload = new { codigo = dto.Codigo, codEmp = inst.CodEmp };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                try
                {
                    await _httpClient.PostAsync(BAILEYS_BASE + "/instancia/desconectar", content);
                }
                catch { }

                await AtualizarStatusBancoAsync(dto.Codigo, "desconectado", null, null, inst.CodEmp);

                _monitor.LogBaileysStatus($"Instância #{dto.Codigo} desconectada manualmente", connected: false);  // ← ADD

                return Ok(new { sucesso = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao desconectar instância {Codigo}", dto.Codigo);
                _monitor.LogException(LogCategory.Baileys, ex,                          // ← ADD
                    $"Desconectar instância #{dto.Codigo}");                            // ← ADD
                return StatusCode(500, new { erro = ex.Message });
            }
        }

        // =========================================================
        // EXCLUIR INSTÂNCIA
        // =========================================================
        [HttpPost("excluir")]
        public async Task<IActionResult> Excluir([FromBody] CodigoDto dto)
        {
            try
            {
                // Derruba no Node antes de remover do banco
                try
                {
                    var payload = new { codigo = dto.Codigo };
                    var json = JsonSerializer.Serialize(payload);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    await _httpClient.PostAsync(BAILEYS_BASE + "/instancia/desconectar", content);
                }
                catch { }

                using var conn = GetConn();
                await conn.OpenAsync();

                using var cmd = new SqlCommand(
                    "DELETE FROM WhatsAppInstancia WHERE Codigo = @Codigo AND CodEmp = @CodEmp", conn);
                AddCodEmpOpcional(cmd, ResolverCodEmp(dto.CodEmp));
                cmd.Parameters.AddWithValue("@Codigo", dto.Codigo);
                await cmd.ExecuteNonQueryAsync();

                return Ok(new { sucesso = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao excluir instância {Codigo}", dto.Codigo);
                return StatusCode(500, new { erro = ex.Message });
            }
        }




        [HttpPost("servico/iniciar")]
        public IActionResult IniciarServico()
        {
            try
            {
                if (VerificarPortaAtiva())
                {
                    _monitor.LogBaileysStatus("Serviço BaileysHelp já estava rodando", connected: true);  // ← ADD
                    return Ok(new { sucesso = true, mensagem = "Serviço já está rodando." });
                }

                ExecutarComando("sc", "start BaileysHelp");

                for (int i = 0; i < 15; i++)
                {
                    System.Threading.Thread.Sleep(1000);
                    if (VerificarPortaAtiva())
                    {
                        _monitor.LogBaileysStatus("Serviço BaileysHelp iniciado com sucesso", connected: true);  // ← ADD
                        return Ok(new { sucesso = true, mensagem = "Serviço iniciado." });
                    }
                }

                _monitor.Log(LogCategory.Baileys, LogSeverity.Warning,                  // ← ADD
                    "BaileysHelp iniciado mas porta 3333 ainda não respondeu em 15s");  // ← ADD

                return Ok(new { sucesso = true, mensagem = "Processo iniciado, porta ainda subindo." });
            }
            catch (Exception ex)
            {
                _monitor.LogException(LogCategory.Baileys, ex, "IniciarServico BaileysHelp");  // ← ADD
                return StatusCode(500, new { sucesso = false, erro = ex.Message });
            }
        }

        private void ExecutarComando(string arquivo, string argumentos)
        {
            var ps = new System.Diagnostics.Process();
            ps.StartInfo.FileName = arquivo;
            ps.StartInfo.Arguments = argumentos;
            ps.StartInfo.UseShellExecute = false;
            ps.StartInfo.RedirectStandardOutput = true;
            ps.StartInfo.RedirectStandardError = true;
            ps.StartInfo.CreateNoWindow = true;
            ps.Start();
            ps.WaitForExit(8000);
            if (!ps.HasExited) ps.Kill();
            ps.Dispose();
        }

        [HttpGet("servico/status")]
        public IActionResult StatusServico()
        {
            try
            {
                bool rodando = VerificarPortaAtiva();
                return Ok(new { rodando });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { sucesso = false, erro = ex.Message });
            }
        }

        private bool VerificarPortaAtiva()
        {
            try
            {
                using var client = new System.Net.Sockets.TcpClient();
                var task = client.ConnectAsync("127.0.0.1", 3333);
                if (!task.Wait(600)) return false;   // timeout = porta morta
                return client.Connected && !task.IsFaulted;
            }
            catch { return false; }
        }

        [HttpPost("servico/parar")]
        public IActionResult PararServico()
        {
            try
            {
                ExecutarComando("sc", "stop BaileysHelp");

                for (int i = 0; i < 15; i++)
                {
                    System.Threading.Thread.Sleep(1000);
                    if (!VerificarPortaAtiva())
                    {
                        _monitor.LogBaileysStatus("Serviço BaileysHelp parado com sucesso", connected: false);  // ← ADD
                        return Ok(new { sucesso = true, mensagem = "Serviço parado." });
                    }
                }

                _monitor.Log(LogCategory.Baileys, LogSeverity.Warning,                          // ← ADD
                    "BaileysHelp: comando de parada enviado mas porta 3333 ainda responde");    // ← ADD

                return Ok(new { sucesso = false, mensagem = "Serviço parado (pode demorar alguns segundos para a porta liberar)." });
            }
            catch (Exception ex)
            {
                _monitor.LogException(LogCategory.Baileys, ex, "PararServico BaileysHelp");  // ← ADD
                return StatusCode(500, new { sucesso = false, erro = ex.Message });
            }
        }

        // =========================================================
        // ATUALIZAR STATUS
        // Chamado pelo Node (index.js) via notificarStatus()
        // Recebe: QR Code, conectado, desconectado
        // =========================================================
        [HttpPost("atualizar-status")]
        public async Task<IActionResult> AtualizarStatus([FromBody] AtualizarStatusDto dto)
        {
            try
            {
                await AtualizarStatusBancoAsync(
                    dto.Codigo,
                    dto.Status,
                    dto.QrCodeBase64,
                    dto.Numero,
                    dto.CodEmp);

                // Conectado ou desconectado são os eventos mais relevantes para o monitor
                if (dto.Status == "conectado")
                    _monitor.LogBaileysStatus(
                        $"Instância #{dto.Codigo} conectada — número: {dto.Numero ?? "?"}",
                        connected: true);
                else if (dto.Status == "desconectado")
                {
                    var msg = string.IsNullOrWhiteSpace(dto.MotivoDesconexao)
                        ? $"Instância #{dto.Codigo} desconectada"
                        : $"Instância #{dto.Codigo} desconectada — {dto.MotivoDesconexao}";

                    _monitor.LogBaileysStatus(msg, connected: false);
                }
                else if (dto.Status == "erro_repasse")
                    _monitor.Log(LogCategory.Baileys, LogSeverity.Error,
                        $"Instância #{dto.Codigo} falhou ao repassar mensagem para o .NET");
                else if (dto.Status == "erro_boot")
                    _monitor.Log(LogCategory.Sistema, LogSeverity.Critical,
                        "BaileysHelp falhou ao restaurar instâncias no boot");                                                                // ← ADD

                return Ok(new { sucesso = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao atualizar status da instância {Codigo}", dto.Codigo);
                _monitor.LogException(LogCategory.Baileys, ex,                                  // ← ADD
                    $"AtualizarStatus instância #{dto.Codigo} → {dto.Status}");                // ← ADD
                return StatusCode(500, new { erro = ex.Message });
            }
        }

        // =========================================================
        // PING
        // Chamado pelo Node a cada 30s para manter DataUltimoPing atualizado
        // A tela Blazor usa isso para saber se o Node ainda está vivo
        // =========================================================
        [HttpPost("ping")]
        public async Task<IActionResult> Ping([FromBody] CodigoDto dto)
        {
            try
            {
                using var conn = GetConn();
                await conn.OpenAsync();

                using var cmd = new SqlCommand(@"
UPDATE WhatsAppInstancia
SET DataUltimoPing = GETDATE()
WHERE Codigo = @Codigo
  AND (@CodEmp = 0 OR CodEmp = @CodEmp)", conn);

                AddCodEmpOpcional(cmd, dto.CodEmp);
                cmd.Parameters.AddWithValue("@Codigo", dto.Codigo);
                await cmd.ExecuteNonQueryAsync();

                return Ok(new { sucesso = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no ping da instância {Codigo}", dto.Codigo);
                return StatusCode(500, new { erro = ex.Message });
            }
        }

        // =========================================================
        // BUSCAR QR CODE
        // A tela Blazor faz polling neste endpoint enquanto aguarda conexão
        // =========================================================
        [HttpGet("qr/{codigo}")]
        public async Task<IActionResult> BuscarQr(int codigo, [FromQuery] int codEmp = 0)
        {
            try
            {
                using var conn = GetConn();
                await conn.OpenAsync();

                using var cmd = new SqlCommand(@"
SELECT Status, QrCodeBase64, Numero
FROM WhatsAppInstancia
WHERE Codigo = @Codigo
  AND CodEmp = @CodEmp", conn);

                AddCodEmpOpcional(cmd, ResolverCodEmp(codEmp));
                cmd.Parameters.AddWithValue("@Codigo", codigo);

                using var rd = await cmd.ExecuteReaderAsync();
                if (!await rd.ReadAsync())
                    return NotFound(new { erro = "Instância não encontrada" });

                return Ok(new
                {
                    status = rd.IsDBNull(0) ? "desconectado" : rd.GetString(0),
                    qrCodeBase64 = rd.IsDBNull(1) ? null : rd.GetString(1),
                    numero = rd.IsDBNull(2) ? null : rd.GetString(2)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao buscar QR da instância {Codigo}", codigo);
                return StatusCode(500, new { erro = ex.Message });
            }
        }

        [HttpPost("forcar-novo-qr")]
        public async Task<IActionResult> ForcarNovoQr([FromBody] ForcarNovoQrRequest req)
        {
            try
            {
                // Usa o helper já existente no controller
                var instancia = await BuscarInstanciaAsync(req.Codigo, req.CodEmp);
                if (instancia == null)
                    return NotFound(new { erro = "Instância não encontrada." });

                var payload = new
                {
                    codigo = instancia.Codigo,
                    pastaAuth = instancia.PastaAuth,
                    nome = instancia.Nome,
                    codEmp = instancia.CodEmp
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Usa _httpClient já existente no controller
                var resp = await _httpClient.PostAsync(
                    BAILEYS_BASE + "/instancia/forcar-novo-qr", content);

                if (!resp.IsSuccessStatusCode)
                {
                    var erro = await resp.Content.ReadAsStringAsync();
                    _monitor.Log(LogCategory.Baileys, LogSeverity.Error,
                        $"Falha ao forçar novo QR instância #{req.Codigo}",
                        erro.Length > 150 ? erro[..150] : erro);
                    return StatusCode(500, new { erro = "Erro ao comunicar com Baileys." });
                }

                // Atualiza status no banco — mesmo Codigo, só muda status
                await AtualizarStatusBancoAsync(req.Codigo, "aguardando_qr", null, null, instancia.CodEmp);

                _monitor.LogBaileysStatus(
                    $"Instância #{req.Codigo} ({instancia.Nome}) — novo QR forçado",
                    connected: false);

                return Ok(new { sucesso = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao forçar novo QR instância {Codigo}", req.Codigo);
                _monitor.LogException(LogCategory.Baileys, ex,
                    $"ForcarNovoQr instância #{req.Codigo}");
                return StatusCode(500, new { erro = ex.Message });
            }
        }

        // =========================================================
        // RECEBER MENSAGEM DO CLIENTE
        // Chamado pelo Node/Baileys quando chega uma mensagem nova.
        // =========================================================
        [HttpPost("receber-mensagem")]
        [HttpPost("mensagem-recebida")]
        public async Task<IActionResult> ReceberMensagem([FromBody] MensagemWhatsAppRecebidaDto dto)
        {
            try
            {
                var telefone = PrimeiroValor(dto.Telefone, dto.From, dto.Numero, dto.RemoteJid);
                var texto = PrimeiroValor(dto.Texto, dto.Mensagem, dto.Body, dto.Message);
                var codInstancia = dto.CodInstancia > 0 ? dto.CodInstancia : dto.Codigo;
                var codEmp = dto.CodEmp > 0 ? dto.CodEmp : 0;

                if (string.IsNullOrWhiteSpace(telefone))
                    return BadRequest(new { erro = "Telefone não informado." });

                if (string.IsNullOrWhiteSpace(texto))
                    texto = "";

                if (codEmp <= 0 && codInstancia > 0)
                {
                    var instancia = await BuscarInstanciaSemFiltroEmpresaAsync(codInstancia);
                    codEmp = instancia?.CodEmp ?? 0;
                }

                if (codEmp <= 0)
                    codEmp = 2;

                await _ticketService.ProcessarMensagemWhatsAppAsync(
                    telefone,
                    texto,
                    codInstancia,
                    codEmp);

                return Ok(new { sucesso = true, codInstancia, codEmp });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao receber mensagem WhatsApp");
                _monitor.LogException(LogCategory.Baileys, ex, "Receber mensagem WhatsApp");
                return StatusCode(500, new { erro = ex.Message });
            }
        }

        // =========================================================
        // HELPERS PRIVADOS
        // =========================================================

        private async Task AtualizarStatusBancoAsync(
            int codigo, string status, string? qrCodeBase64, string? numero, int codEmp = 0)
        {
            using var conn = GetConn();
            await conn.OpenAsync();

            using var cmd = new SqlCommand(@"
UPDATE WhatsAppInstancia
SET
    Status       = @Status,
    QrCodeBase64 = CASE
                       WHEN @Status IN ('conectado','desconectado') THEN NULL
                       WHEN @QrCodeBase64 IS NOT NULL THEN @QrCodeBase64
                       ELSE QrCodeBase64
                   END,
    Numero       = CASE WHEN @Numero IS NOT NULL THEN @Numero ELSE Numero END,
    DataConexao  = CASE WHEN @Status = 'conectado' THEN GETDATE() ELSE DataConexao END
WHERE Codigo = @Codigo
  AND (@CodEmp = 0 OR CodEmp = @CodEmp)", conn);

            AddCodEmpOpcional(cmd, codEmp);
            cmd.Parameters.AddWithValue("@Codigo", codigo);
            cmd.Parameters.AddWithValue("@Status", status ?? "desconectado");
            cmd.Parameters.Add("@QrCodeBase64", SqlDbType.NVarChar, -1).Value =
                (object?)qrCodeBase64 ?? DBNull.Value;
            cmd.Parameters.Add("@Numero", SqlDbType.VarChar, 30).Value =
                (object?)numero ?? DBNull.Value;

            await cmd.ExecuteNonQueryAsync();
        }

        private int ResolverCodEmp(int codEmp) =>
            codEmp > 0 ? codEmp : CodEmpAtual;

        private static string PrimeiroValor(params string?[] valores)
        {
            foreach (var valor in valores)
            {
                if (!string.IsNullOrWhiteSpace(valor))
                    return valor.Trim();
            }

            return "";
        }

        private async Task<InstanciaInfo?> BuscarInstanciaSemFiltroEmpresaAsync(int codigo)
        {
            if (codigo <= 0)
                return null;

            using var conn = GetConn();
            await conn.OpenAsync();

            using var cmd = new SqlCommand(
                "SELECT Codigo, Nome, PastaAuth, Status, CodEmp FROM WhatsAppInstancia WHERE Codigo = @Codigo",
                conn);
            cmd.Parameters.AddWithValue("@Codigo", codigo);

            using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return null;

            return new InstanciaInfo
            {
                Codigo = rd.GetInt32(0),
                Nome = rd.IsDBNull(1) ? "" : rd.GetString(1),
                PastaAuth = rd.IsDBNull(2) ? "" : rd.GetString(2),
                Status = rd.IsDBNull(3) ? "desconectado" : rd.GetString(3),
                CodEmp = rd.IsDBNull(4) ? 0 : rd.GetInt32(4)
            };
        }

        private async Task<InstanciaInfo?> BuscarInstanciaAsync(int codigo, int codEmp = 0)
        {
            using var conn = GetConn();
            await conn.OpenAsync();

            using var cmd = new SqlCommand(
                "SELECT Codigo, Nome, PastaAuth, Status, CodEmp FROM WhatsAppInstancia WHERE Codigo = @Codigo AND CodEmp = @CodEmp",
                conn);
            AddCodEmpOpcional(cmd, ResolverCodEmp(codEmp));
            cmd.Parameters.AddWithValue("@Codigo", codigo);

            using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return null;

            return new InstanciaInfo
            {
                Codigo = rd.GetInt32(0),
                Nome = rd.IsDBNull(1) ? "" : rd.GetString(1),
                PastaAuth = rd.IsDBNull(2) ? "" : rd.GetString(2),
                Status = rd.IsDBNull(3) ? "desconectado" : rd.GetString(3),
                CodEmp = rd.IsDBNull(4) ? 0 : rd.GetInt32(4)
            };
        }

        // =========================================================
        // DTOs
        // =========================================================

        public class CriarInstanciaDto
        {
            public string Nome { get; set; } = "";
            public int CodEmp { get; set; }
        }

        public class CodigoDto
        {
            public int Codigo { get; set; }
            public int CodEmp { get; set; }
        }

        public class AtualizarStatusDto
        {
            public int Codigo { get; set; }
            public string Status { get; set; } = "";
            public string? QrCodeBase64 { get; set; }
            public string? Numero { get; set; }
            public int CodEmp { get; set; }
            public string? MotivoDesconexao { get; set; }   // ← ADD
        }

        public class ForcarNovoQrRequest
        {
            public int Codigo { get; set; }
            public int CodEmp { get; set; }
        }

        public class MensagemWhatsAppRecebidaDto
        {
            public string? Telefone { get; set; }
            public string? Texto { get; set; }
            public string? Mensagem { get; set; }
            public string? From { get; set; }
            public string? Numero { get; set; }
            public string? Body { get; set; }
            public string? Message { get; set; }
            public string? RemoteJid { get; set; }
            public int Codigo { get; set; }
            public int CodInstancia { get; set; }
            public int CodEmp { get; set; }

            [JsonPropertyName("cod_instancia")]
            public int CodInstanciaSnake
            {
                get => CodInstancia;
                set => CodInstancia = value;
            }

            [JsonPropertyName("cod_emp")]
            public int CodEmpSnake
            {
                get => CodEmp;
                set => CodEmp = value;
            }
        }

        private class InstanciaInfo
        {
            public int Codigo { get; set; }
            public string Nome { get; set; } = "";
            public string PastaAuth { get; set; } = "";
            public string Status { get; set; } = "";
            public int CodEmp { get; set; }
        }
    }
}
