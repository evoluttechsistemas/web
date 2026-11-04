using EvolutCRM.Models;
using EvolutCRM.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace EvolutCRM.Controllers
{
    [ApiController]
    [Route("api/docs")]
    public class DocsController : ControllerBase
    {
        private readonly DocService _docService;

        public DocsController(DocService docService)
        {
            _docService = docService;
        }

        [HttpGet("search")]
        public async Task<ActionResult<List<DocSearchItem>>> Search([FromQuery] string q)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 3)
                return Ok(new List<DocSearchItem>());

            var docs = await _docService.SearchAsync(q.Trim(), top: 10, CancellationToken.None);

            var result = docs.Select(d =>
            {
                var textoLimpo = StripHtml(d.Content ?? "");

                return new DocSearchItem
                {
                    Id = d.Id,
                    CodEmp = d.CodEmp,
                    Title = d.Title ?? "",
                    Slug = d.Slug ?? "",
                    Preview = textoLimpo.Length > 160
                        ? textoLimpo.Substring(0, 160) + "..."
                        : textoLimpo,
                    Url = $"https://help.evoluttech.com/curso-tutorial/{d.CodEmp}/{d.Slug}"
                };
            }).ToList();

            return Ok(result);
        }

        private static string StripHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return "";

            var semTags = Regex.Replace(html, "<.*?>", " ");
            semTags = System.Net.WebUtility.HtmlDecode(semTags);
            semTags = Regex.Replace(semTags, @"\s+", " ");

            return semTags.Trim();
        }
    }
}