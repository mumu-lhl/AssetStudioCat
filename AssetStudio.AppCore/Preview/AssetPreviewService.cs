using System.Text;
using AssetStudio.AppCore.Caching;
using AssetStudio.AppCore.Configuration;
using AssetStudio.AppCore.Indexing;
using AssetStudio.AppCore.Loading;
using global::AssetStudio;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AssetStudio.AppCore.Preview;

public sealed class AssetPreviewService
{
    private const int MaximumTextCharacters = 500_000;
    private const int MaximumMeshTriangles = 20_000;
    private readonly AssetObjectLoader _objectLoader;
    private readonly MemoryPreviewCache _cache;
    private readonly AppSettings _settings;

    public AssetPreviewService(AssetObjectLoader objectLoader, MemoryPreviewCache cache, AppSettings settings)
    {
        _objectLoader = objectLoader;
        _cache = cache;
        _settings = settings;
    }

    public bool Supports(string typeName) => typeName is
        "Texture2D" or "Sprite" or "Mesh" or "TextAsset" or "Shader" or "AudioClip" or "MonoScript";

    public async Task<AssetPreview> LoadAsync(
        AssetIndexEntry entry,
        CancellationToken cancellationToken = default)
    {
        if (!Supports(entry.TypeName))
        {
            throw new NotSupportedException($"Preview for {entry.TypeName} is not implemented.");
        }

        var cacheKey = $"{entry.TypeName}\n{entry.ObjectSourcePath}\n{entry.SerializedFile}\n{entry.PathId}";
        if (entry.TypeName is "Texture2D" or "Sprite" or "Mesh" && _cache.TryGet(cacheKey, out var cached))
        {
            return new AssetPreview(cached, null, DescribeBasic(entry, "Decoded preview cache"), true);
        }

        var needsGraph = entry.TypeName == "Sprite";
        using var session = await (needsGraph
            ? _objectLoader.OpenDependencyGraphAsync(entry, cancellationToken)
            : _objectLoader.OpenAsync(entry, cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();

        var preview = session.Asset switch
        {
            Texture2D texture => PreviewTexture(entry, texture),
            Sprite sprite => PreviewSprite(entry, sprite),
            Mesh mesh => PreviewMesh(entry, mesh, cancellationToken),
            TextAsset text => PreviewText(entry, DecodeText(text.m_Script)),
            Shader shader => PreviewText(entry, shader.Convert()),
            AudioClip audio => PreviewAudio(entry, audio),
            MonoScript script => PreviewMonoScript(entry, script),
            _ => throw new NotSupportedException($"Preview for {entry.TypeName} is not implemented."),
        };
        if (preview.PngData is not null)
        {
            _cache.Set(cacheKey, preview.PngData);
        }
        return preview;
    }

    private static AssetPreview PreviewTexture(AssetIndexEntry entry, Texture2D texture)
    {
        using var stream = texture.ConvertToStream(ImageFormat.Png, flip: true)
            ?? throw new NotSupportedException($"Texture format {texture.m_TextureFormat} could not be decoded.");
        return new AssetPreview(
            stream.ToArray(),
            null,
            $"{entry.Name}\nTexture2D\n{texture.m_Width} × {texture.m_Height}\nFormat: {texture.m_TextureFormat}\n" +
            $"PathID: {entry.PathId}\nStored size: {entry.ByteSize:N0} bytes");
    }

    private AssetPreview PreviewSprite(AssetIndexEntry entry, Sprite sprite)
    {
        using var image = sprite.GetImage(_settings.ExportSpriteWithMask ? SpriteMaskMode.On : SpriteMaskMode.Off)
            ?? throw new InvalidDataException("The Sprite texture or atlas dependency could not be resolved.");
        using var stream = image.ConvertToStream(ImageFormat.Png);
        return new AssetPreview(
            stream.ToArray(),
            null,
            $"{entry.Name}\nSprite\n{image.Width} × {image.Height}\nPathID: {entry.PathId}");
    }

    private static AssetPreview PreviewText(AssetIndexEntry entry, string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            throw new InvalidDataException("This asset contains no displayable text.");
        }
        var truncated = text.Length > MaximumTextCharacters;
        var previewText = truncated
            ? text[..MaximumTextCharacters] + "\n\n[Preview truncated. Use converted or dump export for the complete text.]"
            : text;
        return new AssetPreview(
            null,
            previewText,
            DescribeBasic(entry, truncated ? "Text preview truncated" : "Text preview"));
    }

    private static AssetPreview PreviewMonoScript(AssetIndexEntry entry, MonoScript script)
    {
        var qualifiedName = string.IsNullOrWhiteSpace(script.m_Namespace)
            ? script.m_ClassName
            : $"{script.m_Namespace}.{script.m_ClassName}";
        var details = new StringBuilder()
            .AppendLine($"Name: {entry.Name}")
            .AppendLine($"Class: {DisplayValue(qualifiedName)}")
            .AppendLine($"Namespace: {DisplayValue(script.m_Namespace)}")
            .AppendLine($"Assembly: {DisplayValue(script.m_AssemblyName)}")
            .AppendLine($"PathID: {entry.PathId}")
            .AppendLine($"Stored size: {entry.ByteSize:N0} bytes")
            .ToString();
        return new AssetPreview(null, details, DescribeBasic(entry, "MonoScript metadata"));
    }

    private static string DisplayValue(string? value) => string.IsNullOrWhiteSpace(value) ? "(not available)" : value;

    private static AssetPreview PreviewAudio(AssetIndexEntry entry, AudioClip audio)
    {
        var details = new StringBuilder()
            .AppendLine($"Name: {entry.Name}")
            .AppendLine($"Length: {audio.m_Length:0.###} seconds")
            .AppendLine($"Channels: {audio.m_Channels}")
            .AppendLine($"Sample rate: {audio.m_Frequency:N0} Hz")
            .AppendLine($"Bits per sample: {audio.m_BitsPerSample}")
            .AppendLine($"Compression: {(audio.version >= 5 ? audio.m_CompressionFormat : audio.m_Type)}")
            .AppendLine($"Encoded size: {audio.m_AudioData.Size:N0} bytes")
            .ToString();
        return new AssetPreview(null, details, DescribeBasic(entry, "AudioClip metadata"));
    }

    private static AssetPreview PreviewMesh(
        AssetIndexEntry entry,
        Mesh mesh,
        CancellationToken cancellationToken)
    {
        mesh.ProcessData();
        if (mesh.m_VertexCount <= 0 || mesh.m_Vertices is not { Length: > 0 })
        {
            throw new InvalidDataException("The Mesh contains no vertices.");
        }

        const int width = 800;
        const int height = 800;
        var stride = mesh.m_Vertices.Length == mesh.m_VertexCount * 4 ? 4 : 3;
        var projected = new (float X, float Y)[mesh.m_VertexCount];
        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;
        for (var index = 0; index < mesh.m_VertexCount; index++)
        {
            var x = mesh.m_Vertices[index * stride];
            var y = mesh.m_Vertices[index * stride + 1];
            var z = mesh.m_Vertices[index * stride + 2];
            var px = x - z * 0.5f;
            var py = y + (x + z) * 0.25f;
            projected[index] = (px, py);
            minX = Math.Min(minX, px);
            minY = Math.Min(minY, py);
            maxX = Math.Max(maxX, px);
            maxY = Math.Max(maxY, py);
        }
        var scale = Math.Min((width - 40f) / Math.Max(0.0001f, maxX - minX),
            (height - 40f) / Math.Max(0.0001f, maxY - minY));
        using var image = new Image<Rgba32>(width, height, new Rgba32(28, 31, 36));
        var triangleCount = Math.Min(mesh.m_Indices.Count / 3, MaximumMeshTriangles);
        for (var triangle = 0; triangle < triangleCount; triangle++)
        {
            if ((triangle & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            var a = (int)mesh.m_Indices[triangle * 3];
            var b = (int)mesh.m_Indices[triangle * 3 + 1];
            var c = (int)mesh.m_Indices[triangle * 3 + 2];
            if ((uint)a >= (uint)projected.Length || (uint)b >= (uint)projected.Length || (uint)c >= (uint)projected.Length)
            {
                continue;
            }
            DrawLine(image, ToPixel(projected[a]), ToPixel(projected[b]));
            DrawLine(image, ToPixel(projected[b]), ToPixel(projected[c]));
            DrawLine(image, ToPixel(projected[c]), ToPixel(projected[a]));
        }
        using var output = new MemoryStream();
        image.SaveAsPng(output);
        return new AssetPreview(
            output.ToArray(),
            null,
            $"{entry.Name}\nMesh\nVertices: {mesh.m_VertexCount:N0}\nTriangles shown: {triangleCount:N0} / {mesh.m_Indices.Count / 3:N0}\nPathID: {entry.PathId}");

        (int X, int Y) ToPixel((float X, float Y) point) => (
            (int)((point.X - minX) * scale + 20),
            height - 1 - (int)((point.Y - minY) * scale + 20));
    }

    private static void DrawLine(Image<Rgba32> image, (int X, int Y) start, (int X, int Y) end)
    {
        var x = start.X;
        var y = start.Y;
        var dx = Math.Abs(end.X - x);
        var sx = x < end.X ? 1 : -1;
        var dy = -Math.Abs(end.Y - y);
        var sy = y < end.Y ? 1 : -1;
        var error = dx + dy;
        while (true)
        {
            if ((uint)x < (uint)image.Width && (uint)y < (uint)image.Height)
            {
                image[x, y] = new Rgba32(182, 200, 220);
            }
            if (x == end.X && y == end.Y)
            {
                return;
            }
            var twiceError = error * 2;
            if (twiceError >= dy)
            {
                error += dy;
                x += sx;
            }
            if (twiceError <= dx)
            {
                error += dx;
                y += sy;
            }
        }
    }

    private static string DecodeText(byte[] data)
    {
        var text = Encoding.UTF8.GetString(data);
        var controls = text.Count(character => char.IsControl(character) && character is not '\r' and not '\n' and not '\t');
        return controls > Math.Max(8, text.Length / 20)
            ? "[Binary TextAsset. Use converted or raw export to save its bytes.]"
            : text;
    }

    private static string DescribeBasic(AssetIndexEntry entry, string kind) =>
        $"{entry.Name}\n{entry.TypeName}\nPathID: {entry.PathId}\nStored size: {entry.ByteSize:N0} bytes\n{kind}";
}
