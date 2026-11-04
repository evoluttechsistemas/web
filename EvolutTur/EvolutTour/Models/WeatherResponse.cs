namespace EvolutTur.Models;

public class WeatherResponse
{
    public MainInfo Main { get; set; }
    public List<WeatherInfo> Weather { get; set; }
    public WindInfo Wind { get; set; }
    public string Name { get; set; }
}

public class MainInfo
{
    public double Temp { get; set; }
    public double Feels_Like { get; set; }
    public int Humidity { get; set; }
}

public class WeatherInfo
{
    public string Description { get; set; }
    public string Icon { get; set; }
}

public class WindInfo
{
    public double Speed { get; set; }
}
