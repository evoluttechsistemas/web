using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace EvolutTech.IA.Painel.Services;

public sealed record YouTubeSettings(bool Enabled=true,string ChannelUrl="https://www.youtube.com/@evoluttech7275",string ChannelId="UCJ95JKodmws25c07LItAN2g");
public sealed record VideoReference(string Id,string Title,string Url,string Description,DateTimeOffset Published);
public sealed record VideoCatalog(DateTimeOffset UpdatedAt,List<VideoReference> Videos);
public sealed class VideoCatalogService(HttpClient client,PanelStore store)
{
    public Task<YouTubeSettings?> ReadSettingsAsync()=>store.ReadAsync<YouTubeSettings>("youtube-settings.json");
    public async Task<VideoCatalog> GetAsync()=>await store.ReadAsync<VideoCatalog>("youtube-videos.json")??new(DateTimeOffset.MinValue,[]);
    public async Task SaveSettingsAsync(YouTubeSettings settings)
    {
        if(!Regex.IsMatch(settings.ChannelId,@"\AUC[A-Za-z0-9_-]{22}\z")||!Uri.TryCreate(settings.ChannelUrl,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host is not ("www.youtube.com" or "youtube.com"))
            throw new ArgumentException("Informe um canal YouTube HTTPS e um ID de canal válido.");
        await store.WriteAsync("youtube-settings.json",settings);
    }
    public async Task<VideoCatalog> RefreshAsync(CancellationToken token=default)
    {
        var settings=await ReadSettingsAsync()??new();await SaveSettingsAsync(settings);
        using var response=await client.GetAsync("https://www.youtube.com/feeds/videos.xml?channel_id="+settings.ChannelId,HttpCompletionOption.ResponseHeadersRead,token);
        response.EnsureSuccessStatusCode();await using var input=await response.Content.ReadAsStreamAsync(token);
        using var buffer=new MemoryStream();var bytes=new byte[8192];int count;
        while((count=await input.ReadAsync(bytes,token))>0) {if(buffer.Length+count>2_000_000) throw new IOException("O catálogo excedeu o limite permitido.");await buffer.WriteAsync(bytes.AsMemory(0,count),token);}
        buffer.Position=0;var catalog=new VideoCatalog(DateTimeOffset.UtcNow,Parse(buffer,settings.ChannelId));
        await store.WriteAsync("youtube-videos.json",catalog);return catalog;
    }
    public static List<VideoReference> Parse(Stream input,string expectedChannel)
    {
        using var reader=XmlReader.Create(input,new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=2_000_000});
        var xml=XDocument.Load(reader);XNamespace atom="http://www.w3.org/2005/Atom",yt="http://www.youtube.com/xml/schemas/2015",media="http://search.yahoo.com/mrss/";
        var channel=xml.Root?.Element(yt+"channelId")?.Value;
        var canonical=xml.Root?.Elements(atom+"link").Any(l=>(string?)l.Attribute("rel")=="alternate"&&(string?)l.Attribute("href")=="https://www.youtube.com/channel/"+expectedChannel)==true;
        if(channel!=expectedChannel&&!(expectedChannel.StartsWith("UC",StringComparison.Ordinal)&&channel==expectedChannel[2..]&&canonical)) throw new InvalidDataException("O feed não corresponde ao canal configurado.");
        var result=new List<VideoReference>();
        foreach(var entry in xml.Root!.Elements(atom+"entry").Take(30))
        {
            if(entry.Element(yt+"channelId") is { } owner&&owner.Value!=expectedChannel) continue;
            var id=entry.Element(yt+"videoId")?.Value??"";if(!Regex.IsMatch(id,@"\A[A-Za-z0-9_-]{11}\z")) continue;
            var title=ExternalText.Plain(entry.Element(atom+"title")?.Value??"");
            if(title.Length==0) continue;
            DateTimeOffset.TryParse(entry.Element(atom+"published")?.Value,out var published);
            result.Add(new(id,title[..Math.Min(title.Length,200)],"https://www.youtube.com/watch?v="+id,ExternalText.Plain(entry.Element(media+"group")?.Element(media+"description")?.Value??""),published));
        }
        return result.DistinctBy(v=>v.Id).ToList();
    }
    public async Task<List<VideoReference>> RecommendAsync(string question,CancellationToken token)
    {
        var settings=await ReadSettingsAsync()??new();if(!settings.Enabled) return [];
        var catalog=await GetAsync();
        if(catalog.UpdatedAt==DateTimeOffset.MinValue)
            try {catalog=await RefreshAsync(token);} catch(Exception ex) when(ex is HttpRequestException or IOException or XmlException or TaskCanceledException) {if(token.IsCancellationRequested) throw;return [];}
        return Rank(catalog.Videos,question);
    }
    public static List<VideoReference> Rank(IEnumerable<VideoReference> videos,string question)
    {
        string Canonical(string term)=>term.StartsWith("fech",StringComparison.Ordinal)?"fech":term.StartsWith("produt",StringComparison.Ordinal)?"produt":term.StartsWith("cadastr",StringComparison.Ordinal)?"cadastr":term;
        var module=KnowledgeSearch.ModuleOf(question);var terms=KnowledgeSearch.Terms(question).Where(t=>!t.Equals(module,StringComparison.OrdinalIgnoreCase)).Select(Canonical).Distinct().ToArray();
        return videos.Select(v=>new {Video=v,Module=KnowledgeSearch.ModuleOf(v.Title),Title=KnowledgeSearch.Normalize(v.Title)})
            .Where(v=>module.Length==0||v.Module.Length==0||v.Module==module)
            .Select(v=>new {v.Video,Score=terms.Count(t=>v.Title.Contains(t,StringComparison.Ordinal))})
            .Where(v=>v.Score>=Math.Min(2,Math.Max(terms.Length,1))).OrderByDescending(v=>v.Score).Take(2).Select(v=>v.Video).ToList();
    }
}
