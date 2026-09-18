namespace AssetStudio.AppCore.Decompilation;

/// <summary>
/// Helper to automatically detect Unity Managed assembly directories near opened files or folders.
/// </summary>
public static class AssemblyDirectoryDetector
{
    private static readonly string[] MarkerAssemblies =
    [
        "Assembly-CSharp.dll",
        "UnityEngine.dll",
        "UnityEngine.CoreModule.dll",
        "mscorlib.dll"
    ];

    /// <summary>
    /// Attempts to detect a Managed directory containing assemblies based on an opened path.
    /// </summary>
    public static string? TryDetectManagedDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            string startDirectory;
            if (File.Exists(path))
            {
                startDirectory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;
            }
            else if (Directory.Exists(path))
            {
                startDirectory = Path.GetFullPath(path);
            }
            else
            {
                return null;
            }

            if (string.IsNullOrEmpty(startDirectory) || !Directory.Exists(startDirectory))
            {
                return null;
            }

            // Check if startDirectory itself is a Managed folder
            if (IsValidManagedDirectory(startDirectory))
            {
                return startDirectory;
            }

            // Direct subfolder "Managed"
            var directManaged = Path.Combine(startDirectory, "Managed");
            if (IsValidManagedDirectory(directManaged))
            {
                return directManaged;
            }

            // Parent's "Managed"
            var parent = Path.GetDirectoryName(startDirectory);
            if (!string.IsNullOrEmpty(parent))
            {
                var parentManaged = Path.Combine(parent, "Managed");
                if (IsValidManagedDirectory(parentManaged))
                {
                    return parentManaged;
                }

                // Grandparent's "Managed" (e.g. inside StreamingAssets/bundles)
                var grandparent = Path.GetDirectoryName(parent);
                if (!string.IsNullOrEmpty(grandparent))
                {
                    var grandparentManaged = Path.Combine(grandparent, "Managed");
                    if (IsValidManagedDirectory(grandparentManaged))
                    {
                        return grandparentManaged;
                    }
                }
            }

            // Check *_Data/Managed in startDirectory or parent
            var found = ScanForDataManaged(startDirectory);
            if (found is not null)
            {
                return found;
            }

            if (!string.IsNullOrEmpty(parent))
            {
                found = ScanForDataManaged(parent);
                if (found is not null)
                {
                    return found;
                }
            }

            // macOS .app bundle path: Contents/Resources/Data/Managed
            var macManaged = Path.Combine(startDirectory, "Contents", "Resources", "Data", "Managed");
            if (IsValidManagedDirectory(macManaged))
            {
                return macManaged;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ScanForDataManaged(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return null;
            }

            foreach (var subDir in Directory.EnumerateDirectories(directory, "*_Data", SearchOption.TopDirectoryOnly))
            {
                var managed = Path.Combine(subDir, "Managed");
                if (IsValidManagedDirectory(managed))
                {
                    return managed;
                }
            }
        }
        catch
        {
            // ignored
        }
        return null;
    }

    public static bool IsValidManagedDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return false;
        }

        try
        {
            var files = Directory.GetFiles(directory, "*.dll");
            if (files.Length == 0)
            {
                return false;
            }

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                foreach (var marker in MarkerAssemblies)
                {
                    if (string.Equals(fileName, marker, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
            // ignored
        }
        return false;
    }
}
