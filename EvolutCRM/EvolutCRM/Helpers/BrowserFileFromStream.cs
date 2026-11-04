using Microsoft.AspNetCore.Components.Forms;

public class BrowserFileFromStream : IBrowserFile
{
    private readonly Stream _stream;

    public BrowserFileFromStream(Stream stream, string contentType, string name)
    {
        _stream = stream;
        ContentType = contentType;
        Name = name;
        Size = stream.Length;
        LastModified = DateTimeOffset.Now;
    }

    public string Name { get; }
    public DateTimeOffset LastModified { get; }
    public long Size { get; }
    public string ContentType { get; }

    public Stream OpenReadStream(
        long maxAllowedSize,
        CancellationToken cancellationToken = default)
    {
        return _stream;
    }
}
