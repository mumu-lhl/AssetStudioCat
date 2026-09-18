namespace AssetStudio
{
    public static class Gltf
    {
        public enum Format
        {
            Glb,   // Binary glTF (.glb) - Single container file (recommended)
            Gltf,  // Standard glTF (.gltf) - JSON text with embedded/external resources
        }

        public class Settings
        {
            public Format ExportFormat { get; set; } = Format.Glb;
            public bool ExportAnimations { get; set; } = true;
            public bool ExportSkins { get; set; } = true;
            public bool ExportBlendShapes { get; set; } = true;
            public float ScaleFactor { get; set; } = 1.0f;
            public bool EmbedImages { get; set; } = true;

            public static string GetFileExtension(Format format) => format switch
            {
                Format.Gltf => ".gltf",
                _ => ".glb",
            };
        }
    }
}
