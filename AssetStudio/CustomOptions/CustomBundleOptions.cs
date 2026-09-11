using System;
using System.IO;

namespace AssetStudio.CustomOptions
{
    public class CustomBundleOptions
    {
        private CompressionType _customBlockCompression = CompressionType.Auto;
        private CompressionType _customBlockInfoCompression = CompressionType.Auto;
        private bool _decompressToDisk;
        private string _decompressionDirectory;

        public ImportOptions Options;

        public CompressionType CustomBlockCompression
        {
            get => _customBlockCompression;
            set => _customBlockCompression = SetOption(nameof(CustomBlockCompression), value);
        }
        public CompressionType CustomBlockInfoCompression
        {
            get => _customBlockInfoCompression;
            set => _customBlockInfoCompression = SetOption(nameof(CustomBlockInfoCompression), value);
        }
        public bool DecompressToDisk
        {
            get => _decompressToDisk;
            set => _decompressToDisk = SetOption(nameof(DecompressToDisk), value);
        }
        public string DecompressionDirectory
        {
            get => _decompressionDirectory;
            set => _decompressionDirectory = SetDecompressionDirectory(value);
        }

        public CustomBundleOptions() { }

        public CustomBundleOptions(ImportOptions importOptions)
        {
            Options = importOptions;
        }

        private static T SetOption<T>(string option, T value)
        {
            Logger.Info($"- {option}: {value}");
            return value;
        }

        private string SetDecompressionDirectory(string value)
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? null
                : Path.GetFullPath(Environment.ExpandEnvironmentVariables(value));
            Logger.Info($"- {nameof(DecompressionDirectory)}: {normalized ?? "Default"}");
            return normalized;
        }
    }
}
