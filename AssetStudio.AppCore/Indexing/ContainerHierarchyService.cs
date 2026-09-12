namespace AssetStudio.AppCore.Indexing;

public sealed class ContainerHierarchyService
{
    public Task<IReadOnlyList<ContainerHierarchyNode>> BuildAsync(
        DiskAssetIndex index,
        CancellationToken cancellationToken = default) =>
        BuildAsync(index.EnumerateAsync(cancellationToken: cancellationToken), cancellationToken);

    public async Task<IReadOnlyList<ContainerHierarchyNode>> BuildAsync(
        IAsyncEnumerable<AssetIndexEntry> entries,
        CancellationToken cancellationToken = default)
    {
        var rootDir = new MutableNode("Root", "", true);
        var noContainerDir = new MutableNode("(No Container)", "(No Container)", false);
        int totalCount = 0;

        await foreach (var entry in entries.WithCancellation(cancellationToken))
        {
            totalCount++;
            if (string.IsNullOrWhiteSpace(entry.Container))
            {
                noContainerDir.DirectAssetCount++;
                continue;
            }

            var normalized = entry.Container.Replace('\\', '/').Trim('/');
            var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                noContainerDir.DirectAssetCount++;
                continue;
            }

            var current = rootDir;
            var currentPath = "";

            for (int i = 0; i < segments.Length; i++)
            {
                var seg = segments[i];
                currentPath = string.IsNullOrEmpty(currentPath) ? seg : currentPath + "/" + seg;
                var isLeaf = (i == segments.Length - 1);

                var child = current.Children.Find(c => c.Name.Equals(seg, StringComparison.OrdinalIgnoreCase));
                if (child is null)
                {
                    child = new MutableNode(seg, currentPath, isDirectory: !isLeaf);
                    current.Children.Add(child);
                }
                else if (!isLeaf)
                {
                    child.IsDirectory = true;
                }
                current = child;
            }

            current.DirectAssetCount++;
        }

        int ComputeAssetCounts(MutableNode node)
        {
            var sum = node.DirectAssetCount;
            foreach (var child in node.Children)
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
        ComputeAssetCounts(noContainerDir);

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

        result.AddRange(SortNodes(rootDir.Children).Select(ToImmutable));

        if (noContainerDir.TotalAssetCount > 0)
        {
            result.Add(ToImmutable(noContainerDir));
        }

        return result;
    }

    private static IEnumerable<MutableNode> SortNodes(IEnumerable<MutableNode> nodes) =>
        nodes.OrderByDescending(n => n.IsDirectory)
             .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase);

    private static ContainerHierarchyNode ToImmutable(MutableNode node) => new(
        node.Name,
        node.FullPath,
        node.IsDirectory,
        node.TotalAssetCount,
        null,
        SortNodes(node.Children).Select(ToImmutable).ToArray());

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
        public List<MutableNode> Children { get; } = [];
    }
}
