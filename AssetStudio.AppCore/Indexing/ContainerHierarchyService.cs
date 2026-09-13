using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AssetStudio.AppCore.Indexing;

public sealed class ContainerHierarchyService
{
    private const uint CacheMagic = 0x43544143; // "CATC"
    private const uint CacheVersion = 1;
    private const string CacheFileName = "containers.bin";

    public async Task<IReadOnlyList<ContainerHierarchyNode>> BuildAsync(
        DiskAssetIndex index,
        CancellationToken cancellationToken = default)
    {
        var cachePath = Path.Combine(index.DirectoryPath, CacheFileName);
        if (File.Exists(cachePath))
        {
            try
            {
                var cached = await TryLoadFromCacheAsync(cachePath, cancellationToken);
                if (cached is not null)
                {
                    return cached;
                }
            }
            catch
            {
                // Fallback to building from index entries if cache read fails
            }
        }

        var result = await BuildAsync(index.EnumerateAsync(cancellationToken: cancellationToken), cancellationToken);

        try
        {
            await SaveToCacheAsync(cachePath, result, cancellationToken);
        }
        catch
        {
            // Non-fatal if cache save fails
        }

        return result;
    }

    public async Task<IReadOnlyList<ContainerHierarchyNode>> BuildAsync(
        IAsyncEnumerable<AssetIndexEntry> entries,
        CancellationToken cancellationToken = default)
    {
        var containerCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        int totalCount = 0;
        int noContainerCount = 0;

        await foreach (var entry in entries.WithCancellation(cancellationToken))
        {
            totalCount++;
            if (string.IsNullOrWhiteSpace(entry.Container))
            {
                noContainerCount++;
                continue;
            }

            ref int count = ref CollectionsMarshal.GetValueRefOrAddDefault(containerCounts, entry.Container, out _);
            count++;
        }

        var rootDir = new MutableNode("Root", "", true);

        foreach (var (container, count) in containerCounts)
        {
            var normalized = container.Replace('\\', '/').Trim('/');
            var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                noContainerCount += count;
                continue;
            }

            var current = rootDir;
            var currentPath = "";

            for (int i = 0; i < segments.Length; i++)
            {
                var seg = segments[i];
                currentPath = string.IsNullOrEmpty(currentPath) ? seg : currentPath + "/" + seg;
                var isLeaf = (i == segments.Length - 1);

                if (!current.Children.TryGetValue(seg, out var child))
                {
                    child = new MutableNode(seg, currentPath, isDirectory: !isLeaf);
                    current.Children[seg] = child;
                }
                else if (!isLeaf)
                {
                    child.IsDirectory = true;
                }
                current = child;
            }

            current.DirectAssetCount += count;
        }

        int ComputeAssetCounts(MutableNode node)
        {
            var sum = node.DirectAssetCount;
            foreach (var child in node.Children.Values)
            {
                sum += ComputeAssetCounts(child);
            }
            node.TotalAssetCount = sum;
            if (node.Children.Count > 0)
            {
                node.IsDirectory = true;
            }
            return sum;
        }

        ComputeAssetCounts(rootDir);

        var result = new List<ContainerHierarchyNode>();

        if (totalCount > 0)
        {
            result.Add(new ContainerHierarchyNode(
                "AllAssets",
                "",
                IsDirectory: true,
                totalCount,
                null,
                []));
        }

        var sortedRoots = rootDir.Children.Values
            .OrderByDescending(n => n.IsDirectory)
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ToImmutable);
        result.AddRange(sortedRoots);

        if (noContainerCount > 0)
        {
            result.Add(new ContainerHierarchyNode(
                "(No Container)",
                "(No Container)",
                IsDirectory: false,
                noContainerCount,
                null,
                []));
        }

        return result;
    }

    private static ContainerHierarchyNode ToImmutable(MutableNode node)
    {
        var children = node.Children.Values
            .OrderByDescending(n => n.IsDirectory)
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ToImmutable)
            .ToArray();

        return new ContainerHierarchyNode(
            node.Name,
            node.FullPath,
            node.IsDirectory,
            node.TotalAssetCount,
            null,
            children);
    }

    private static async Task SaveToCacheAsync(
        string cachePath,
        IReadOnlyList<ContainerHierarchyNode> roots,
        CancellationToken cancellationToken)
    {
        var tmpPath = cachePath + ".tmp";
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 65536);
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);

            writer.Write(CacheMagic);
            writer.Write(CacheVersion);
            writer.Write(roots.Count);

            foreach (var root in roots)
            {
                WriteNode(writer, root);
            }
        }, cancellationToken);

        File.Move(tmpPath, cachePath, overwrite: true);
    }

    private static void WriteNode(BinaryWriter writer, ContainerHierarchyNode node)
    {
        writer.Write(node.Name);
        writer.Write(node.FullPath);
        writer.Write(node.IsDirectory);
        writer.Write(node.TotalAssetCount);
        writer.Write(node.Children.Count);
        for (int i = 0; i < node.Children.Count; i++)
        {
            WriteNode(writer, node.Children[i]);
        }
    }

    private static async Task<IReadOnlyList<ContainerHierarchyNode>?> TryLoadFromCacheAsync(
        string cachePath,
        CancellationToken cancellationToken)
    {
        return await Task.Run<IReadOnlyList<ContainerHierarchyNode>?>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 65536);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

            if (reader.ReadUInt32() != CacheMagic) return null;
            if (reader.ReadUInt32() != CacheVersion) return null;

            int rootCount = reader.ReadInt32();
            if (rootCount < 0 || rootCount > 10_000) return null;

            var roots = new ContainerHierarchyNode[rootCount];
            for (int i = 0; i < rootCount; i++)
            {
                roots[i] = ReadNode(reader);
            }
            return roots;
        }, cancellationToken);
    }

    private static ContainerHierarchyNode ReadNode(BinaryReader reader)
    {
        var name = reader.ReadString();
        var fullPath = reader.ReadString();
        var isDirectory = reader.ReadBoolean();
        var totalAssetCount = reader.ReadInt32();
        var childrenCount = reader.ReadInt32();

        ContainerHierarchyNode[] children;
        if (childrenCount <= 0)
        {
            children = [];
        }
        else
        {
            children = new ContainerHierarchyNode[childrenCount];
            for (int i = 0; i < childrenCount; i++)
            {
                children[i] = ReadNode(reader);
            }
        }

        return new ContainerHierarchyNode(name, fullPath, isDirectory, totalAssetCount, null, children);
    }

    private sealed class MutableNode(
        string name,
        string fullPath,
        bool isDirectory)
    {
        public string Name { get; } = name;
        public string FullPath { get; } = fullPath;
        public bool IsDirectory { get; set; } = isDirectory;
        public int DirectAssetCount { get; set; }
        public int TotalAssetCount { get; set; }
        public Dictionary<string, MutableNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
