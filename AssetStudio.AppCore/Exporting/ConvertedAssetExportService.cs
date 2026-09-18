using System.Buffers;
using System.Text;
using AssetStudio.AppCore.Indexing;
using AssetStudio.AppCore.Loading;
using AssetStudio.AppCore.Configuration;
using AssetStudio.AppCore.Decompilation;
using global::AssetStudio;

namespace AssetStudio.AppCore.Exporting;

public sealed class ConvertedAssetExportService
{
    private const int BufferSize = 128 * 1024;
    private readonly AssetObjectLoader _objectLoader;
    private readonly AppSettings _settings;
    private readonly ICSharpDecompilerService? _decompilerService;

    public ConvertedAssetExportService(
        AssetObjectLoader objectLoader,
        AppSettings settings,
        ICSharpDecompilerService? decompilerService = null)
    {
        _objectLoader = objectLoader;
        _settings = settings;
        _decompilerService = decompilerService;
    }

    public Task<AssetExportResult> ExportAsync(
        AssetIndexEntry entry,
        string outputDirectory,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Export(entry, outputDirectory, cancellationToken), cancellationToken);

    private AssetExportResult Export(
        AssetIndexEntry entry,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var needsGraph = entry.TypeName.Equals(nameof(ClassIDType.Sprite), StringComparison.Ordinal);
        using var session = (needsGraph
                ? _objectLoader.OpenDependencyGraphAsync(entry, cancellationToken)
                : _objectLoader.OpenAsync(entry, cancellationToken))
            .GetAwaiter()
            .GetResult();
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(outputDirectory);
        return session.Asset switch
        {
            Texture2D texture => ExportTexture(texture, entry, outputDirectory, _settings.ConvertedImageFormat),
            Sprite sprite => ExportSprite(sprite, entry, outputDirectory, _settings.ConvertedImageFormat, _settings.ExportSpriteWithMask),
            AudioClip audio => ExportAudio(audio, entry, outputDirectory, _settings.ConvertAudioToWav),
            VideoClip video => ExportVideo(video, entry, outputDirectory),
            MovieTexture movie => ExportBytes(movie.m_MovieData, entry, outputDirectory, ".ogv"),
            Shader shader => ExportText(shader.Convert(), entry, outputDirectory, ".shader"),
            TextAsset text => ExportBytes(text.m_Script, entry, outputDirectory, ResolveTextExtension(entry, text)),
            MonoBehaviour mono => ExportText(
                mono.DumpObject() ?? throw new NotSupportedException("The MonoBehaviour could not be converted to JSON."),
                entry,
                outputDirectory,
                ".json"),
            Material material => ExportText(
                material.Dump() ?? material.DumpObject() ?? throw new NotSupportedException("The Material could not be converted to text."),
                entry,
                outputDirectory,
                ".txt"),
            Font font => ExportFont(font, entry, outputDirectory),
            Mesh mesh => ExportMesh(mesh, entry, outputDirectory, cancellationToken),
            MonoScript monoScript => ExportMonoScript(monoScript, entry, outputDirectory, cancellationToken),
            _ => throw new NotSupportedException($"Converted export for {entry.TypeName} is not implemented."),
        };
    }

    private AssetExportResult ExportMonoScript(
        MonoScript script,
        AssetIndexEntry entry,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        string? code = null;
        if (_decompilerService is not null)
        {
            code = _decompilerService.DecompileTypeAsync(
                script.m_AssemblyName,
                script.m_ClassName,
                script.m_Namespace,
                cancellationToken).GetAwaiter().GetResult();
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            var sb = new StringBuilder();
            sb.AppendLine($"// Decompilation stub for {entry.Name}");
            sb.AppendLine($"// Assembly: {script.m_AssemblyName}");
            sb.AppendLine($"// PathID: {entry.PathId}");
            sb.AppendLine($"// Notice: Assembly '{script.m_AssemblyName}' could not be resolved. Configure the Managed directory to decompile full source.");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(script.m_Namespace))
            {
                sb.AppendLine($"namespace {script.m_Namespace}");
                sb.AppendLine("{");
                sb.AppendLine($"    public class {script.m_ClassName}");
                sb.AppendLine("    {");
                sb.AppendLine("    }");
                sb.AppendLine("}");
            }
            else
            {
                sb.AppendLine($"public class {script.m_ClassName}");
                sb.AppendLine("{");
                sb.AppendLine("}");
            }
            code = sb.ToString();
        }

        var path = GetAvailablePath(outputDirectory, entry, ".cs");
        WriteAtomically(path, temporaryPath =>
        {
            using var writer = new StreamWriter(temporaryPath, false, new UTF8Encoding(false), BufferSize);
            writer.Write(code);
        });
        return new AssetExportResult([path]);
    }

    private static AssetExportResult ExportTexture(
        Texture2D texture,
        AssetIndexEntry entry,
        string directory,
        ConvertedImageFormat format)
    {
        using var image = texture.ConvertToImage(flip: true)
            ?? throw new InvalidDataException("The texture format could not be decoded.");
        var path = GetAvailablePath(directory, entry, GetImageExtension(format));
        WriteAtomically(path, temporaryPath =>
        {
            using var output = File.Create(temporaryPath);
            image.WriteToStream(output, ToImageFormat(format));
        });
        return new AssetExportResult([path]);
    }

    private static AssetExportResult ExportSprite(
        Sprite sprite,
        AssetIndexEntry entry,
        string directory,
        ConvertedImageFormat format,
        bool exportMask)
    {
        using var image = sprite.GetImage(exportMask ? SpriteMaskMode.Export : SpriteMaskMode.Off)
            ?? throw new InvalidDataException("The Sprite texture or atlas dependency could not be resolved.");
        var path = GetAvailablePath(directory, entry, GetImageExtension(format));
        WriteAtomically(path, temporaryPath =>
        {
            using var output = File.Create(temporaryPath);
            image.WriteToStream(output, ToImageFormat(format));
        });
        return new AssetExportResult([path]);
    }

    private static AssetExportResult ExportAudio(
        AudioClip audio,
        AssetIndexEntry entry,
        string directory,
        bool convertToWav)
    {
        var size = audio.m_AudioData.Size;
        if (size <= 0)
        {
            throw new InvalidDataException("The AudioClip contains no audio data.");
        }

        var source = ArrayPool<byte>.Shared.Rent(size);
        try
        {
            var read = audio.m_AudioData.GetData(source);
            if (read <= 0)
            {
                throw new EndOfStreamException("The AudioClip data could not be read.");
            }

            try
            {
                var converter = new AudioClipConverter(audio);
                if (convertToWav && (converter.IsSupport || converter.IsLegacy))
                {
                    var log = string.Empty;
                    var wav = converter.IsLegacy
                        ? converter.RawAudioClipToWav(ref log)
                        : converter.ConvertToWav(source, ref log);
                    if (wav is not null)
                    {
                        return ExportBytes(wav, entry, directory, ".wav");
                    }
                }
                return ExportBuffer(source, read, entry, directory, converter.GetExtensionName(),
                    "This audio codec was preserved without conversion.");
            }
            catch (Exception exception) when (exception is TypeInitializationException
                                               or DllNotFoundException
                                               or BadImageFormatException
                                               or EntryPointNotFoundException)
            {
                return ExportBuffer(source, read, entry, directory, ".audio",
                    "FMOD is unavailable, so the original encoded audio was exported.");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(source, clearArray: true);
        }
    }

    private static AssetExportResult ExportVideo(VideoClip video, AssetIndexEntry entry, string directory)
    {
        if (video.m_ExternalResources.m_Size <= 0)
        {
            throw new InvalidDataException("The VideoClip contains no video data.");
        }
        var extension = Path.GetExtension(video.m_OriginalPath);
        if (string.IsNullOrWhiteSpace(extension) || extension.Any(character => !char.IsLetterOrDigit(character) && character != '.'))
        {
            extension = ".video";
        }
        var path = GetAvailablePath(directory, entry, extension);
        WriteAtomically(path, video.m_VideoData.WriteData);
        return new AssetExportResult([path]);
    }

    private static AssetExportResult ExportFont(Font font, AssetIndexEntry entry, string directory)
    {
        if (font.m_FontData is not { Length: > 0 } data)
        {
            throw new InvalidDataException("The Font contains no embedded font data.");
        }
        var extension = data.Length >= 4 && data[0] == (byte)'O' && data[1] == (byte)'T'
            && data[2] == (byte)'T' && data[3] == (byte)'O'
            ? ".otf"
            : ".ttf";
        return ExportBytes(data, entry, directory, extension);
    }

    private static AssetExportResult ExportMesh(
        Mesh mesh,
        AssetIndexEntry entry,
        string directory,
        CancellationToken cancellationToken)
    {
        mesh.ProcessData();
        if (mesh.m_VertexCount <= 0 || mesh.m_Vertices is not { Length: > 0 })
        {
            throw new InvalidDataException("The Mesh contains no vertices.");
        }

        var path = GetAvailablePath(directory, entry, ".obj");
        WriteAtomically(path, temporaryPath =>
        {
            using var writer = new StreamWriter(temporaryPath, false, new UTF8Encoding(false), BufferSize);
            writer.WriteLine($"g {MakeSafeFileName(mesh.m_Name)}");
            var vertexStride = mesh.m_Vertices.Length == mesh.m_VertexCount * 4 ? 4 : 3;
            for (var vertex = 0; vertex < mesh.m_VertexCount; vertex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.WriteLine(FormattableString.Invariant(
                    $"v {-mesh.m_Vertices[vertex * vertexStride]} {mesh.m_Vertices[vertex * vertexStride + 1]} {mesh.m_Vertices[vertex * vertexStride + 2]}"));
            }

            var uvStride = mesh.m_UV0?.Length == mesh.m_VertexCount * 2 ? 2
                : mesh.m_UV0?.Length == mesh.m_VertexCount * 3 ? 3 : 4;
            if (mesh.m_UV0 is { Length: > 0 })
            {
                for (var vertex = 0; vertex < mesh.m_VertexCount; vertex++)
                {
                    writer.WriteLine(FormattableString.Invariant(
                        $"vt {mesh.m_UV0[vertex * uvStride]} {mesh.m_UV0[vertex * uvStride + 1]}"));
                }
            }

            var normalStride = mesh.m_Normals?.Length == mesh.m_VertexCount * 4 ? 4 : 3;
            if (mesh.m_Normals is { Length: > 0 })
            {
                for (var vertex = 0; vertex < mesh.m_VertexCount; vertex++)
                {
                    writer.WriteLine(FormattableString.Invariant(
                        $"vn {-mesh.m_Normals[vertex * normalStride]} {mesh.m_Normals[vertex * normalStride + 1]} {mesh.m_Normals[vertex * normalStride + 2]}"));
                }
            }

            var triangle = 0;
            for (var subMesh = 0; subMesh < mesh.m_SubMeshes.Count; subMesh++)
            {
                writer.WriteLine($"g {MakeSafeFileName(mesh.m_Name)}_{subMesh}");
                var triangleEnd = triangle + (int)mesh.m_SubMeshes[subMesh].indexCount / 3;
                while (triangle < triangleEnd)
                {
                    var a = mesh.m_Indices[triangle * 3 + 2] + 1;
                    var b = mesh.m_Indices[triangle * 3 + 1] + 1;
                    var c = mesh.m_Indices[triangle * 3] + 1;
                    writer.WriteLine(FormattableString.Invariant($"f {a}/{a}/{a} {b}/{b}/{b} {c}/{c}/{c}"));
                    triangle++;
                }
            }
        });
        return new AssetExportResult([path]);
    }

    private static string ResolveTextExtension(AssetIndexEntry entry, TextAsset text)
    {
        if (Path.HasExtension(text.m_Name))
        {
            return string.Empty;
        }
        var containerExtension = Path.GetExtension(entry.Container);
        return string.IsNullOrWhiteSpace(containerExtension) ? ".txt" : containerExtension;
    }

    private static ImageFormat ToImageFormat(ConvertedImageFormat format) => format switch
    {
        ConvertedImageFormat.Jpeg => ImageFormat.Jpeg,
        ConvertedImageFormat.Webp => ImageFormat.Webp,
        ConvertedImageFormat.Bmp => ImageFormat.Bmp,
        ConvertedImageFormat.Tga => ImageFormat.Tga,
        _ => ImageFormat.Png,
    };

    private static string GetImageExtension(ConvertedImageFormat format) =>
        "." + format.ToString().ToLowerInvariant();

    private static AssetExportResult ExportText(string text, AssetIndexEntry entry, string directory, string extension) =>
        ExportBytes(new UTF8Encoding(false).GetBytes(text), entry, directory, extension);

    private static AssetExportResult ExportBytes(byte[] data, AssetIndexEntry entry, string directory, string extension) =>
        ExportBuffer(data, data.Length, entry, directory, extension);

    private static AssetExportResult ExportBuffer(
        byte[] data,
        int length,
        AssetIndexEntry entry,
        string directory,
        string extension,
        string? note = null)
    {
        var path = GetAvailablePath(directory, entry, extension);
        WriteAtomically(path, temporaryPath =>
        {
            using var output = File.Create(temporaryPath);
            output.Write(data, 0, length);
        });
        return new AssetExportResult([path], note);
    }

    private static string GetAvailablePath(string directory, AssetIndexEntry entry, string extension)
    {
        var name = MakeSafeFileName(entry.Name);
        var candidate = Path.Combine(Path.GetFullPath(directory), name + extension);
        if (!File.Exists(candidate) && !Directory.Exists(candidate))
        {
            return candidate;
        }
        candidate = Path.Combine(Path.GetFullPath(directory), $"{name}_{entry.PathId}{extension}");
        return File.Exists(candidate) || Directory.Exists(candidate)
            ? Path.Combine(Path.GetFullPath(directory), $"{name}_{entry.PathId}_{Guid.NewGuid():N}{extension}")
            : candidate;
    }

    private static string MakeSafeFileName(string value)
    {
        const string invalid = "<>:\"/\\|?*";
        var safe = new string(value.Select(character => char.IsControl(character) || invalid.Contains(character) ? '_' : character).ToArray())
            .Trim(' ', '.');
        return string.IsNullOrWhiteSpace(safe) ? "asset" : safe;
    }

    private static void WriteAtomically(string outputPath, Action<string> write)
    {
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(outputPath)!,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            write(temporaryPath);
            File.Move(temporaryPath, outputPath, false);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
