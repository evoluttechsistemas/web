using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Text;

namespace EvolutTech.IA.Painel.Services;

public sealed record ScreenReference(Guid Id,string Module,string Keywords,string Version,string Caption,
    string ImageDataUri,double X,double Y,double Width,double Height,bool Highlight=true);

public static class ScreenIllustration
{
    public const int MaxBytes=4_000_000;
    public static (int Width,int Height) Dimensions(byte[] bytes)
    {
        if(bytes.Length<33||bytes.Length>MaxBytes||!bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})||
            Encoding.ASCII.GetString(bytes,12,4)!="IHDR") throw new ArgumentException("Selecione uma captura PNG de até 4 MB.");
        var w=BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16,4));
        var h=BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20,4));
        if(w<1||h<1||w>12000||h>12000||(long)w*h>40_000_000) throw new ArgumentException("Dimensões da captura inválidas.");
        return (w,h);
    }
    public static void Validate(ScreenReference screen)
    {
        if((screen.Module is not ("Cadastros" or "Aplicação")&&KnowledgeSearch.ModuleOf(screen.Module)!=screen.Module)||string.IsNullOrWhiteSpace(screen.Keywords)||screen.Keywords.Length>160||
            string.IsNullOrWhiteSpace(screen.Caption)||screen.Caption.Length>180||screen.Version.Length>40||
            new[]{screen.X,screen.Y,screen.Width,screen.Height}.Any(v=>!double.IsFinite(v))||
            screen.X<0||screen.Y<0||screen.Width<=0||screen.Height<=0||screen.X+screen.Width>100||screen.Y+screen.Height>100)
            throw new ArgumentException("Confira módulo, palavras da dúvida, legenda e área de destaque (0 a 100%).");
        if(!screen.ImageDataUri.StartsWith("data:image/png;base64,",StringComparison.Ordinal)||screen.ImageDataUri.Length>MaxBytes*4/3+100)
            throw new ArgumentException("Selecione uma captura PNG de até 4 MB.");
        Dimensions(Convert.FromBase64String(screen.ImageDataUri.Split(',')[1]));
    }
    public static ScreenReference? Find(IEnumerable<ScreenReference> screens,string question,string version)
    {
        var module=ApplicationSearchIntent.PreferredForm(question) is not null?"Cadastros":KnowledgeSearch.ModuleOf(question);
        var terms=KnowledgeSearch.Terms(question);
        return screens.Where(s=>s.Module==module&&(s.Version.Length==0||s.Version==version))
            .Where(s=>s.Keywords.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries) is {Length:>0} keys&&
                keys.All(k=>KnowledgeSearch.Terms(k) is {Length:>0} required&&required.All(terms.Contains)))
            .OrderByDescending(s=>s.Version.Length>0).FirstOrDefault();
    }
    public static string Render(ScreenReference screen)
    {
        Validate(screen);
        var dimensions=Dimensions(Convert.FromBase64String(screen.ImageDataUri.Split(',')[1]));
        double height=800d*dimensions.Height/dimensions.Width;
        string N(double value)=>value.ToString("0.##",CultureInfo.InvariantCulture);
        var x=screen.X*8;var y=96+height*screen.Y/100;
        var width=screen.Width*8;var boxHeight=height*screen.Height/100;
        var targetX=x+width/2;
        var caption=ExternalText.CustomerAnswer(screen.Caption);
        var labelLines=caption.Chunk(70).Select(c=>new string(c)).ToArray();
        var label=string.Join("",labelLines.Select((line,i)=>$"<text x=\"20\" y=\"{27+i*22}\" font-size=\"18\" font-family=\"Arial\" fill=\"#123455\">{WebUtility.HtmlEncode(line)}</text>"));
        var overlay=screen.Highlight?$"<rect x=\"{N(x)}\" y=\"{N(y)}\" width=\"{N(width)}\" height=\"{N(boxHeight)}\" rx=\"4\" fill=\"none\" stroke=\"#e34b10\" stroke-width=\"4\"/><path d=\"M400,78 L{N(targetX)},{N(y)}\" stroke=\"#e34b10\" stroke-width=\"3\" fill=\"none\" marker-end=\"url(#arrow)\"/>":"";
        var svg=$"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"800\" height=\"{N(height+96)}\" viewBox=\"0 0 800 {N(height+96)}\"><title>{WebUtility.HtmlEncode(caption)}</title><defs><marker id=\"arrow\" markerWidth=\"8\" markerHeight=\"8\" refX=\"7\" refY=\"4\" orient=\"auto\"><path d=\"M0,0 L8,4 L0,8 Z\" fill=\"#e34b10\"/></marker></defs><rect width=\"100%\" height=\"100%\" fill=\"#fff\"/>{label}<image href=\"{screen.ImageDataUri}\" x=\"0\" y=\"96\" width=\"800\" height=\"{N(height)}\"/>{overlay}</svg>";
        return "data:image/svg+xml;base64,"+Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
    }
}
