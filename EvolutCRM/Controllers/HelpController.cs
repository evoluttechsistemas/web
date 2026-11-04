using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EvolutCRM.Models;
using EvolutCRM.Services;

namespace EvolutCRM.Controllers
{
    // Exige login: aberto, qualquer um na internet podia chamar a IA (gpt-4.1) e gastar o crédito.
    // O Card agora chama o AiService direto, sem passar por aqui.
    [Authorize]
    [ApiController]
    [Route("api/help")]
    public class HelpController : ControllerBase
    {
        private readonly AiService _ai;

        public HelpController(AiService ai)
        {
            _ai = ai;
        }

        [HttpPost("suggest")]
        public async Task<IActionResult> Suggest([FromBody] AiChatRequest request, CancellationToken ct)
        {
            var reply = await _ai.AskAsync(request.Message, request.ConversationContext, ct);
            return Ok(new AiChatResponse { Reply = reply });
        }
    }
}