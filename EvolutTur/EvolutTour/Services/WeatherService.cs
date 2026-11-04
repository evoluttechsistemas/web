using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using EvolutTur.Models;

namespace EvolutTur.Services;

public class WeatherService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string apiKey = "aae881313a90c4e7e231ac7956888f86"; // coloque sua chave

    public WeatherService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<WeatherResponse> GetWeatherAsync(string cidade)
    {
        var client = _httpClientFactory.CreateClient("OpenWeather");
        string url = $"weather?q={cidade}&appid={apiKey}&units=metric&lang=pt_br";

        return await client.GetFromJsonAsync<WeatherResponse>(url);
    }
    public async Task<List<WeatherResponse>> GetWeatherForCitiesAsync(List<string> cidades)
    {
        var client = _httpClientFactory.CreateClient("OpenWeather");
        var results = new List<WeatherResponse>();

        foreach (var cidade in cidades)
        {
            try
            {
                string url = $"weather?q={cidade}&appid={apiKey}&units=metric&lang=pt_br";
                var clima = await client.GetFromJsonAsync<WeatherResponse>(url);
                if (clima != null)
                    results.Add(clima);
            }
            catch { /* Ignora cidades não encontradas */ }
        }

        return results;
    }

}
