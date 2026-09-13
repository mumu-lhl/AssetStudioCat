namespace AssetStudio.AppCore.Preview;

using System.Numerics;
using System.Text;
using AssetStudio.AppCore.Caching;
using AssetStudio.AppCore.Configuration;
using AssetStudio.AppCore.Indexing;
using AssetStudio.AppCore.Loading;
using global::AssetStudio;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Vector3 = System.Numerics.Vector3;

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
        "Texture2D" or "Sprite" or "Mesh" or "TextAsset" or "Shader" or "Material" or "AudioClip" or "MonoScript" or "MonoBehaviour";

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

        var needsGraph = entry.TypeName is "Sprite" or "MonoBehaviour" or "Material";
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
            Material material => PreviewMaterial(entry, material),
            AudioClip audio => PreviewAudio(entry, audio),
            MonoScript script => PreviewMonoScript(entry, script),
            MonoBehaviour monoBehaviour => PreviewMonoBehaviour(entry, monoBehaviour),
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

    private static AssetPreview PreviewMonoBehaviour(AssetIndexEntry entry, MonoBehaviour monoBehaviour)
    {
        var info = new StringBuilder();
        info.AppendLine($"Name: {DisplayValue(entry.Name)}");
        info.AppendLine($"Type: MonoBehaviour");

        if (monoBehaviour.m_Script.TryGet(out var script))
        {
            var qualifiedName = string.IsNullOrWhiteSpace(script.m_Namespace)
                ? script.m_ClassName
                : $"{script.m_Namespace}.{script.m_ClassName}";
            info.AppendLine($"Script: {DisplayValue(qualifiedName)}");
            info.AppendLine($"Namespace: {DisplayValue(script.m_Namespace)}");
            info.AppendLine($"Assembly: {DisplayValue(script.m_AssemblyName)}");
        }
        else
        {
            info.AppendLine("Script: (unresolved)");
        }

        info.AppendLine($"PathID: {entry.PathId}");
        info.AppendLine($"Stored size: {entry.ByteSize:N0} bytes");

        // Try to dump serialized fields via TypeTree, then fall back to JSON object dump.
        var dumpText = monoBehaviour.Dump();
        if (string.IsNullOrEmpty(dumpText))
        {
            dumpText = monoBehaviour.DumpObject();
        }

        if (!string.IsNullOrEmpty(dumpText))
        {
            var truncated = dumpText.Length > MaximumTextCharacters;
            if (truncated)
            {
                dumpText = dumpText[..MaximumTextCharacters]
                    + "\n\n[Preview truncated. Use dump export for the complete object.]";
            }
            return new AssetPreview(null, dumpText, info.ToString());
        }

        // No dump available – show metadata only.
        return new AssetPreview(null, info.ToString(), DescribeBasic(entry, "MonoBehaviour metadata"));
    }

    private static AssetPreview PreviewMaterial(AssetIndexEntry entry, Material material)
    {
        var info = new StringBuilder();
        info.AppendLine($"Name: {DisplayValue(entry.Name)}");
        info.AppendLine("Type: Material");
        info.AppendLine($"PathID: {entry.PathId}");
        info.AppendLine($"Stored size: {entry.ByteSize:N0} bytes");

        if (material.m_Shader.TryGet(out var shader))
        {
            info.AppendLine($"Shader: {shader.m_Name}");
        }
        else
        {
            info.AppendLine($"Shader: (PathID {material.m_Shader.m_PathID})");
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Material: {DisplayValue(entry.Name)}");
        if (material.m_Shader.TryGet(out var s))
        {
            sb.AppendLine($"Shader: {s.m_Name}");
        }
        else
        {
            sb.AppendLine($"Shader: (PathID {material.m_Shader.m_PathID})");
        }
        sb.AppendLine();

        if (material.m_SavedProperties != null)
        {
            if (material.m_SavedProperties.m_TexEnvs?.Count > 0)
            {
                sb.AppendLine("Textures:");
                foreach (var tex in material.m_SavedProperties.m_TexEnvs)
                {
                    var texName = tex.Value.m_Texture.TryGet(out var t) ? t.m_Name : $"(PathID {tex.Value.m_Texture.m_PathID})";
                    sb.AppendLine($"  {tex.Key}: {texName} (scale: {tex.Value.m_Scale}, offset: {tex.Value.m_Offset})");
                }
                sb.AppendLine();
            }

            if (material.m_SavedProperties.m_Colors?.Count > 0)
            {
                sb.AppendLine("Colors:");
                foreach (var col in material.m_SavedProperties.m_Colors)
                {
                    sb.AppendLine($"  {col.Key}: (R: {col.Value.R:F3}, G: {col.Value.G:F3}, B: {col.Value.B:F3}, A: {col.Value.A:F3})");
                }
                sb.AppendLine();
            }

            if (material.m_SavedProperties.m_Floats?.Count > 0)
            {
                sb.AppendLine("Floats:");
                foreach (var flt in material.m_SavedProperties.m_Floats)
                {
                    sb.AppendLine($"  {flt.Key}: {flt.Value}");
                }
                sb.AppendLine();
            }

            if (material.m_SavedProperties.m_Ints?.Count > 0)
            {
                sb.AppendLine("Ints:");
                foreach (var val in material.m_SavedProperties.m_Ints)
                {
                    sb.AppendLine($"  {val.Key}: {val.Value}");
                }
                sb.AppendLine();
            }
        }

        var dumpText = material.Dump();
        if (string.IsNullOrEmpty(dumpText))
        {
            dumpText = material.DumpObject();
        }

        if (!string.IsNullOrEmpty(dumpText))
        {
            sb.AppendLine("--- Serialized Dump ---");
            sb.AppendLine(dumpText);
        }

        var fullText = sb.ToString();
        var truncated = fullText.Length > MaximumTextCharacters;
        if (truncated)
        {
            fullText = fullText[..MaximumTextCharacters] + "\n\n[Preview truncated. Use dump export for the complete object.]";
        }

        return new AssetPreview(null, fullText, info.ToString());
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

        var vertexCount = mesh.m_VertexCount;
        var stride = mesh.m_Vertices.Length == vertexCount * 4 ? 4 : 3;
        var positions = new float[vertexCount * 3];
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        var projected = new (float X, float Y)[vertexCount];
        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;

        for (var index = 0; index < vertexCount; index++)
        {
            var x = mesh.m_Vertices[index * stride];
            var y = mesh.m_Vertices[index * stride + 1];
            var z = mesh.m_Vertices[index * stride + 2];

            positions[index * 3] = x;
            positions[index * 3 + 1] = y;
            positions[index * 3 + 2] = z;

            min.X = Math.Min(min.X, x);
            min.Y = Math.Min(min.Y, y);
            min.Z = Math.Min(min.Z, z);
            max.X = Math.Max(max.X, x);
            max.Y = Math.Max(max.Y, y);
            max.Z = Math.Max(max.Z, z);

            var px = x - z * 0.5f;
            var py = y + (x + z) * 0.25f;
            projected[index] = (px, py);
            minX = Math.Min(minX, px);
            minY = Math.Min(minY, py);
            maxX = Math.Max(maxX, px);
            maxY = Math.Max(maxY, py);
        }

        var totalTriangles = mesh.m_Indices.Count / 3;
        var validIndices = new uint[totalTriangles * 3];
        for (var i = 0; i < totalTriangles * 3; i++)
        {
            validIndices[i] = mesh.m_Indices[i];
        }

        // Normals
        float[] normals;
        float[]? calculatedNormals = null;
        if (mesh.m_Normals != null && mesh.m_Normals.Length > 0)
        {
            normals = new float[vertexCount * 3];
            var normalStride = mesh.m_Normals.Length == vertexCount * 4 ? 4 : 3;
            for (var i = 0; i < vertexCount; i++)
            {
                normals[i * 3] = mesh.m_Normals[i * normalStride];
                normals[i * 3 + 1] = mesh.m_Normals[i * normalStride + 1];
                normals[i * 3 + 2] = mesh.m_Normals[i * normalStride + 2];
            }
            calculatedNormals = CalculateSmoothNormals(positions, validIndices, vertexCount);
        }
        else
        {
            normals = CalculateSmoothNormals(positions, validIndices, vertexCount);
        }

        // Colors
        float[]? colors = null;
        if (mesh.m_Colors != null && mesh.m_Colors.Length > 0)
        {
            var colorStride = mesh.m_Colors.Length == vertexCount * 4 ? 4 : 3;
            colors = new float[vertexCount * 4];
            for (var i = 0; i < vertexCount; i++)
            {
                colors[i * 4] = mesh.m_Colors[i * colorStride];
                colors[i * 4 + 1] = mesh.m_Colors[i * colorStride + 1];
                colors[i * 4 + 2] = mesh.m_Colors[i * colorStride + 2];
                colors[i * 4 + 3] = colorStride == 4 ? mesh.m_Colors[i * colorStride + 3] : 1.0f;
            }
        }

        var center = (min + max) * 0.5f;
        var extents = max - min;
        var boundingRadius = (max - center).Length();
        if (boundingRadius < 0.0001f)
        {
            boundingRadius = 1.0f;
        }

        var geometry = new MeshGeometryData
        {
            Positions = positions,
            Normals = normals,
            CalculatedNormals = calculatedNormals,
            Colors = colors,
            Indices = validIndices,
            VertexCount = vertexCount,
            TriangleCount = totalTriangles,
            Min = min,
            Max = max,
            Center = center,
            Extents = extents,
            BoundingRadius = boundingRadius
        };

        const int width = 800;
        const int height = 800;
        var scale = Math.Min((width - 40f) / Math.Max(0.0001f, maxX - minX),
            (height - 40f) / Math.Max(0.0001f, maxY - minY));
        using var image = new Image<Rgba32>(width, height, new Rgba32(28, 31, 36));
        var triangleCount = Math.Min(totalTriangles, MaximumMeshTriangles);
        for (var triangle = 0; triangle < triangleCount; triangle++)
        {
            if ((triangle & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            var a = (int)validIndices[triangle * 3];
            var b = (int)validIndices[triangle * 3 + 1];
            var c = (int)validIndices[triangle * 3 + 2];
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
            $"{entry.Name}\nMesh\nVertices: {mesh.m_VertexCount:N0}\nTriangles: {totalTriangles:N0}\nPathID: {entry.PathId}",
            false,
            geometry);

        (int X, int Y) ToPixel((float X, float Y) point) => (
            (int)((point.X - minX) * scale + 20),
            height - 1 - (int)((point.Y - minY) * scale + 20));
    }

    private static float[] CalculateSmoothNormals(float[] positions, uint[] indices, int vertexCount)
    {
        var normals = new Vector3[vertexCount];
        var counts = new int[vertexCount];

        for (var i = 0; i < indices.Length; i += 3)
        {
            var i0 = (int)indices[i];
            var i1 = (int)indices[i + 1];
            var i2 = (int)indices[i + 2];

            if ((uint)i0 >= (uint)vertexCount || (uint)i1 >= (uint)vertexCount || (uint)i2 >= (uint)vertexCount)
                continue;

            var v0 = new Vector3(positions[i0 * 3], positions[i0 * 3 + 1], positions[i0 * 3 + 2]);
            var v1 = new Vector3(positions[i1 * 3], positions[i1 * 3 + 1], positions[i1 * 3 + 2]);
            var v2 = new Vector3(positions[i2 * 3], positions[i2 * 3 + 1], positions[i2 * 3 + 2]);

            var dir1 = v1 - v0;
            var dir2 = v2 - v0;
            var normal = Vector3.Cross(dir1, dir2);
            if (normal.LengthSquared() > 1e-10f)
            {
                normal = Vector3.Normalize(normal);
                normals[i0] += normal;
                normals[i1] += normal;
                normals[i2] += normal;
                counts[i0]++;
                counts[i1]++;
                counts[i2]++;
            }
        }

        var result = new float[vertexCount * 3];
        for (var i = 0; i < vertexCount; i++)
        {
            var n = counts[i] > 0 && normals[i].LengthSquared() > 1e-10f
                ? Vector3.Normalize(normals[i])
                : Vector3.UnitY;
            result[i * 3] = n.X;
            result[i * 3 + 1] = n.Y;
            result[i * 3 + 2] = n.Z;
        }
        return result;
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
