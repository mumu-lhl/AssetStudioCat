using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AssetStudio.AppCore.Configuration;
using AssetStudio.AppCore.Indexing;
using AssetStudio.AppCore.Preview;
using AssetStudioGUI.Avalonia.ViewModels;

namespace AssetStudioGUI.Avalonia.Services;

public sealed class AssetStudioHttpServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    private readonly MainViewModel _viewModel;
    private readonly AppDirectories _directories;
    private readonly int _preferredPort;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private int _boundPort;
    private bool _disposed;

    public AssetStudioHttpServer(MainViewModel viewModel, AppDirectories directories, int preferredPort = 23333)
    {
        _viewModel = viewModel;
        _directories = directories;
        _preferredPort = preferredPort;
    }

    public int BoundPort => _boundPort;

    public void Start()
    {
        if (_listener is not null) return;

        int port = _preferredPort;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                var listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                listener.Start();
                _listener = listener;
                _boundPort = port;
                break;
            }
            catch (HttpListenerException)
            {
                port++;
            }
        }

        if (_listener is null)
        {
            return;
        }

        WriteEndpointDiscoveryFile(_boundPort);

        _cts = new CancellationTokenSource();
        _listenTask = Task.Run(() => ListenLoopAsync(_listener, _cts.Token));
    }

    private void WriteEndpointDiscoveryFile(int port)
    {
        try
        {
            var info = new JsonObject
            {
                ["port"] = port,
                ["url"] = $"http://127.0.0.1:{port}/",
                ["pid"] = Environment.ProcessId,
                ["started_at"] = DateTimeOffset.UtcNow.ToString("o")
            };
            var json = info.ToJsonString();

            var configPath = Path.Combine(_directories.ConfigDirectory, "api-endpoint.json");
            File.WriteAllText(configPath, json);

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home) && Directory.Exists(home))
            {
                File.WriteAllText(Path.Combine(home, ".assetstudio_cat_api.json"), json);
            }
        }
        catch
        {
            // Ignore failure writing discovery file
        }
    }

    private async Task ListenLoopAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && listener.IsListening)
        {
            try
            {
                var context = await listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context), cancellationToken);
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested || !listener.IsListening)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch
            {
                // Continue listening
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        // CORS headers
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "*");

        if (request.HttpMethod == "OPTIONS")
        {
            response.StatusCode = (int)HttpStatusCode.NoContent;
            response.Close();
            return;
        }

        response.ContentType = "application/json; charset=utf-8";

        try
        {
            var path = request.Url?.AbsolutePath.TrimEnd('/') ?? "";
            switch (path)
            {
                case "/api/status":
                    await HandleStatusAsync(context);
                    break;
                case "/api/assets/search":
                    await HandleSearchAsync(context);
                    break;
                case "/api/assets/inspect":
                    await HandleInspectAsync(context);
                    break;
                case "/api/assets/references":
                    await HandleReferencesAsync(context);
                    break;
                case "/api/assets/export":
                    if (request.HttpMethod == "POST")
                    {
                        await HandleExportAsync(context);
                    }
                    else
                    {
                        await SendErrorAsync(response, HttpStatusCode.MethodNotAllowed, "Only POST is allowed for export.");
                    }
                    break;
                default:
                    await SendErrorAsync(response, HttpStatusCode.NotFound, $"Endpoint not found: {path}");
                    break;
            }
        }
        catch (Exception ex)
        {
            await SendErrorAsync(response, HttpStatusCode.InternalServerError, ex.Message);
        }
        finally
        {
            try { response.Close(); } catch { }
        }
    }

    private async Task HandleStatusAsync(HttpListenerContext context)
    {
        var index = _viewModel.CurrentIndex;
        var hasSource = _viewModel.HasSource && index is not null;

        var counts = hasSource ? await index!.GetTypeCountsAsync() : new Dictionary<string, long>();
        var total = _viewModel.TotalAssetCount;

        var result = new JsonObject
        {
            ["status"] = "ok",
            ["has_source"] = hasSource,
            ["source_path"] = _viewModel.SourceSummary,
            ["total_assets"] = total,
            ["selected_asset"] = _viewModel.SelectedAsset?.Name,
            ["type_counts"] = JsonSerializer.SerializeToNode(counts)
        };

        await SendJsonAsync(context.Response, result);
    }

    private async Task HandleSearchAsync(HttpListenerContext context)
    {
        var index = _viewModel.CurrentIndex;
        if (index is null)
        {
            await SendErrorAsync(context.Response, HttpStatusCode.BadRequest, "No index is currently open in AssetStudioCat GUI.");
            return;
        }

        var query = context.Request.QueryString;
        var q = query["q"] ?? query["query"];
        var type = query["type"];
        var container = query["container"];

        int limit = 20;
        if (int.TryParse(query["limit"], out var parsedLimit) && parsedLimit > 0)
        {
            limit = Math.Clamp(parsedLimit, 1, 100);
        }

        int offset = 0;
        if (int.TryParse(query["offset"], out var parsedOffset) && parsedOffset >= 0)
        {
            offset = parsedOffset;
        }

        var assetQuery = new AssetIndexQuery(
            SearchText: string.IsNullOrWhiteSpace(q) ? null : q.Trim(),
            TypeName: string.IsNullOrWhiteSpace(type) ? null : type.Trim(),
            ContainerPath: string.IsNullOrWhiteSpace(container) ? null : container.Trim(),
            ExactContainer: false,
            Offset: offset,
            Limit: limit,
            SortField: AssetSortField.IndexOrder,
            SortDescending: false);

        var page = await index.QueryAsync(assetQuery);

        var itemsNode = new JsonArray();
        foreach (var item in page.Items)
        {
            itemsNode.Add(new JsonObject
            {
                ["id"] = item.Id,
                ["name"] = item.Name,
                ["type"] = item.TypeName,
                ["container"] = item.Container,
                ["path_id"] = item.PathId,
                ["size"] = item.ByteSize,
                ["source_file"] = Path.GetFileName(item.SerializedFile)
            });
        }

        var responseObj = new JsonObject
        {
            ["total_matched"] = page.TotalCount ?? page.Items.Count,
            ["offset"] = page.Offset,
            ["limit"] = limit,
            ["has_more"] = page.NextOffset.HasValue,
            ["next_offset"] = page.NextOffset,
            ["items"] = itemsNode
        };

        await SendJsonAsync(context.Response, responseObj);
    }

    private async Task HandleInspectAsync(HttpListenerContext context)
    {
        var index = _viewModel.CurrentIndex;
        if (index is null)
        {
            await SendErrorAsync(context.Response, HttpStatusCode.BadRequest, "No index is currently open in AssetStudioCat GUI.");
            return;
        }

        var query = context.Request.QueryString;
        if (!long.TryParse(query["id"], out var assetId))
        {
            await SendErrorAsync(context.Response, HttpStatusCode.BadRequest, "Missing or invalid 'id' parameter.");
            return;
        }

        var entry = await index.GetByIdAsync(assetId);
        if (entry is null)
        {
            await SendErrorAsync(context.Response, HttpStatusCode.NotFound, $"Asset with id {assetId} not found.");
            return;
        }

        var mode = (query["mode"] ?? "summary").ToLowerInvariant();
        int startLine = int.TryParse(query["start_line"], out var sl) && sl > 0 ? sl : 1;
        int maxLines = int.TryParse(query["max_lines"], out var ml) && ml > 0 ? Math.Clamp(ml, 1, 1000) : 200;
        int maxBytes = int.TryParse(query["max_bytes"], out var mb) && mb > 0 ? Math.Clamp(mb, 512, 1048576) : 16384;

        string? rawText = null;
        string? summaryText = null;
        JsonObject? metadata = null;

        if (_viewModel.PreviewService is { } previewService && previewService.Supports(entry.TypeName))
        {
            try
            {
                var preview = await previewService.LoadAsync(entry);
                rawText = preview.Text;

                if (preview.MeshGeometry is { } mesh)
                {
                    metadata = new JsonObject
                    {
                        ["vertex_count"] = mesh.VertexCount,
                        ["triangle_count"] = mesh.TriangleCount,
                        ["indices_count"] = mesh.Indices.Length,
                        ["has_normals"] = mesh.Normals.Length > 0,
                        ["has_colors"] = mesh.Colors is { Length: > 0 },
                        ["bounding_radius"] = mesh.BoundingRadius
                    };
                }
            }
            catch (Exception ex)
            {
                summaryText = $"Preview unavailable: {ex.Message}";
            }
        }

        // Generate tailored summary if mode == "summary"
        if (mode == "summary")
        {
            if (entry.TypeName == "Shader" && rawText is not null)
            {
                summaryText = ExtractShaderSummary(rawText);
            }
            else if (rawText is not null)
            {
                summaryText = rawText;
            }
            else
            {
                summaryText = $"Asset #{entry.Id} ({entry.TypeName}): {entry.Name}";
            }
        }

        // Apply line slicing and byte limits
        var textToProcess = (mode == "summary" ? summaryText : rawText) ?? "";
        var lines = textToProcess.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        int totalLines = lines.Length;
        int actualStart = Math.Clamp(startLine, 1, Math.Max(1, totalLines));
        int takeCount = Math.Min(maxLines, totalLines - actualStart + 1);
        var slicedLines = lines.Skip(actualStart - 1).Take(takeCount);
        var finalContent = string.Join("\n", slicedLines);

        bool truncatedBytes = false;
        if (Encoding.UTF8.GetByteCount(finalContent) > maxBytes)
        {
            var bytes = Encoding.UTF8.GetBytes(finalContent);
            finalContent = Encoding.UTF8.GetString(bytes, 0, maxBytes) + "\n... [Truncated: byte limit reached]";
            truncatedBytes = true;
        }

        bool hasMore = (actualStart + takeCount - 1 < totalLines) || truncatedBytes;
        int? nextStart = actualStart + takeCount - 1 < totalLines ? actualStart + takeCount : null;

        var result = new JsonObject
        {
            ["id"] = entry.Id,
            ["name"] = entry.Name,
            ["type"] = entry.TypeName,
            ["container"] = entry.Container,
            ["path_id"] = entry.PathId,
            ["size"] = entry.ByteSize,
            ["source_file"] = entry.SerializedFile,
            ["mode"] = mode,
            ["total_lines"] = totalLines,
            ["start_line"] = actualStart,
            ["line_count"] = takeCount,
            ["has_more"] = hasMore,
            ["next_start_line"] = nextStart,
            ["content"] = finalContent
        };

        if (metadata is not null)
        {
            result["metadata"] = metadata;
        }

        await SendJsonAsync(context.Response, result);
    }

    private static string ExtractShaderSummary(string fullText)
    {
        var lines = fullText.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var sb = new StringBuilder();
        bool inProperties = false;
        int propBraceCount = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (trimmed.StartsWith("Shader \"", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine(line);
                continue;
            }

            if (trimmed.StartsWith("Properties", StringComparison.OrdinalIgnoreCase))
            {
                inProperties = true;
                sb.AppendLine(line);
                propBraceCount += CountBraces(line);
                continue;
            }

            if (inProperties)
            {
                sb.AppendLine(line);
                propBraceCount += CountBraces(line);
                if (propBraceCount <= 0 && trimmed.Contains('}'))
                {
                    inProperties = false;
                }
                continue;
            }

            if (trimmed.StartsWith("SubShader", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("Tags", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("Pass", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine(line);
            }
        }

        return sb.Length > 0 ? sb.ToString() : (lines.Length > 150 ? string.Join("\n", lines.Take(150)) : fullText);
    }

    private static int CountBraces(string str)
    {
        int count = 0;
        foreach (var c in str)
        {
            if (c == '{') count++;
            else if (c == '}') count--;
        }
        return count;
    }

    private async Task HandleReferencesAsync(HttpListenerContext context)
    {
        var index = _viewModel.CurrentIndex;
        if (index is null)
        {
            await SendErrorAsync(context.Response, HttpStatusCode.BadRequest, "No index is currently open in AssetStudioCat GUI.");
            return;
        }

        var query = context.Request.QueryString;
        if (!long.TryParse(query["id"], out var assetId))
        {
            await SendErrorAsync(context.Response, HttpStatusCode.BadRequest, "Missing or invalid 'id' parameter.");
            return;
        }

        var entry = await index.GetByIdAsync(assetId);
        if (entry is null)
        {
            await SendErrorAsync(context.Response, HttpStatusCode.NotFound, $"Asset with id {assetId} not found.");
            return;
        }

        int limit = int.TryParse(query["limit"], out var l) && l > 0 ? Math.Clamp(l, 1, 100) : 20;

        var referencesNode = new JsonArray();
        var referencedByNode = new JsonArray();

        // 1. Material -> Shader / Textures
        if (entry.TypeName == "Material" && _viewModel.PreviewService is { } previewService)
        {
            try
            {
                var preview = await previewService.LoadAsync(entry);
                if (preview.Text is not null)
                {
                    // Parse text preview lines for references
                    var lines = preview.Text.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("Shader: ", StringComparison.OrdinalIgnoreCase))
                        {
                            referencesNode.Add(new JsonObject
                            {
                                ["type"] = "Shader",
                                ["name"] = trimmed["Shader: ".Length..]
                            });
                        }
                        else if (trimmed.Contains(':') && !trimmed.StartsWith("Material:") && !trimmed.StartsWith("PathID:") && !trimmed.StartsWith("Stored size:") && !trimmed.StartsWith("Textures:"))
                        {
                            referencesNode.Add(new JsonObject
                            {
                                ["type"] = "Texture2D/Property",
                                ["detail"] = trimmed
                            });
                        }
                    }
                }
            }
            catch
            {
                // Non-fatal
            }
        }

        // 2. Shader -> Find Materials using this Shader
        if (entry.TypeName == "Shader")
        {
            // Search materials that might match the shader name or container
            var shaderName = entry.Name;
            var shortName = Path.GetFileName(shaderName);

            var page = await index.QueryAsync(new AssetIndexQuery(
                SearchText: null,
                TypeName: "Material",
                ContainerPath: null,
                ExactContainer: false,
                Offset: 0,
                Limit: 200,
                SortField: AssetSortField.IndexOrder,
                SortDescending: false));

            int count = 0;
            foreach (var mat in page.Items)
            {
                if (count >= limit) break;
                // If material in same container or shares naming prefix
                if (!string.IsNullOrEmpty(entry.Container) && mat.Container == entry.Container)
                {
                    referencedByNode.Add(new JsonObject
                    {
                        ["id"] = mat.Id,
                        ["name"] = mat.Name,
                        ["type"] = mat.TypeName,
                        ["container"] = mat.Container,
                        ["match_reason"] = "Same container"
                    });
                    count++;
                }
            }
        }

        // 3. GameObject / Transform hierarchy
        if (entry.TypeName is "Transform" or "RectTransform" or "GameObject")
        {
            if (entry.GameObjectPathId.HasValue)
            {
                referencesNode.Add(new JsonObject
                {
                    ["type"] = "GameObject",
                    ["path_id"] = entry.GameObjectPathId.Value,
                    ["file"] = entry.GameObjectSerializedFile ?? entry.SerializedFile
                });
            }
            if (entry.ParentTransformPathId.HasValue)
            {
                referencesNode.Add(new JsonObject
                {
                    ["type"] = "ParentTransform",
                    ["path_id"] = entry.ParentTransformPathId.Value,
                    ["file"] = entry.ParentTransformSerializedFile ?? entry.SerializedFile
                });
            }
        }

        var resultObj = new JsonObject
        {
            ["id"] = entry.Id,
            ["name"] = entry.Name,
            ["type"] = entry.TypeName,
            ["references"] = referencesNode,
            ["referenced_by"] = referencedByNode
        };

        await SendJsonAsync(context.Response, resultObj);
    }

    private async Task HandleExportAsync(HttpListenerContext context)
    {
        var index = _viewModel.CurrentIndex;
        if (index is null)
        {
            await SendErrorAsync(context.Response, HttpStatusCode.BadRequest, "No index is currently open in AssetStudioCat GUI.");
            return;
        }

        using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        var jsonNode = JsonNode.Parse(body);
        if (jsonNode is null)
        {
            await SendErrorAsync(context.Response, HttpStatusCode.BadRequest, "Invalid JSON body.");
            return;
        }

        var outputDir = jsonNode["output_directory"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(outputDir))
        {
            await SendErrorAsync(context.Response, HttpStatusCode.BadRequest, "Missing 'output_directory'.");
            return;
        }

        var idsArray = jsonNode["asset_ids"]?.AsArray();
        if (idsArray is null || idsArray.Count == 0)
        {
            await SendErrorAsync(context.Response, HttpStatusCode.BadRequest, "Missing or empty 'asset_ids'.");
            return;
        }

        Directory.CreateDirectory(outputDir);

        var exportedFiles = new List<string>();
        var errors = new List<string>();

        var exportService = _viewModel.ConvertedExportService;
        foreach (var idNode in idsArray)
        {
            if (idNode is null || !idNode.AsValue().TryGetValue<long>(out var id)) continue;
            var entry = await index.GetByIdAsync(id);
            if (entry is null)
            {
                errors.Add($"Asset #{id} not found");
                continue;
            }

            try
            {
                if (exportService is not null)
                {
                    var res = await exportService.ExportAsync(entry, outputDir);
                    exportedFiles.AddRange(res.Files);
                }
                else
                {
                    errors.Add($"Export service unavailable for #{id}");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to export #{id} ({entry.Name}): {ex.Message}");
            }
        }

        var result = new JsonObject
        {
            ["success"] = errors.Count == 0,
            ["exported_count"] = exportedFiles.Count,
            ["files"] = JsonSerializer.SerializeToNode(exportedFiles),
            ["errors"] = JsonSerializer.SerializeToNode(errors)
        };

        await SendJsonAsync(context.Response, result);
    }

    private static async Task SendJsonAsync(HttpListenerResponse response, JsonNode data)
    {
        response.StatusCode = (int)HttpStatusCode.OK;
        await using var writer = new Utf8JsonWriter(response.OutputStream);
        data.WriteTo(writer);
        await writer.FlushAsync();
    }

    private static async Task SendErrorAsync(HttpListenerResponse response, HttpStatusCode statusCode, string message)
    {
        response.StatusCode = (int)statusCode;
        var errorObj = new JsonObject
        {
            ["error"] = message,
            ["status_code"] = (int)statusCode
        };
        await using var writer = new Utf8JsonWriter(response.OutputStream);
        errorObj.WriteTo(writer);
        await writer.FlushAsync();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        _cts?.Dispose();
    }
}
