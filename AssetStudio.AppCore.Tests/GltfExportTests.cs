using System;
using System.Collections.Generic;
using System.IO;
using AssetStudio;
using SharpGLTF.Schema2;
using Xunit;

namespace AssetStudio.AppCore.Tests;

public class GltfExportTests
{
    private class TestImported : IImported
    {
        public ImportedFrame RootFrame { get; set; } = new ImportedFrame();
        public List<ImportedMesh> MeshList { get; set; } = new();
        public List<ImportedMaterial> MaterialList { get; set; } = new();
        public List<ImportedTexture> TextureList { get; set; } = new();
        public List<ImportedKeyframedAnimation> AnimationList { get; set; } = new();
        public List<ImportedMorph> MorphList { get; set; } = new();
    }

    private static TestImported CreateSampleModel()
    {
        var imported = new TestImported();

        // Hierarchy: Root -> Body -> Arm
        var root = new ImportedFrame { Name = "Root", LocalPosition = Vector3.Zero, LocalScale = Vector3.One };
        var body = new ImportedFrame { Name = "Body", LocalPosition = new Vector3(0, 1, 0), LocalScale = Vector3.One };
        var arm = new ImportedFrame { Name = "Arm", LocalPosition = new Vector3(1, 0, 0), LocalScale = Vector3.One };
        root.AddChild(body);
        body.AddChild(arm);
        imported.RootFrame = root;

        // Material & Texture
        var mat = new ImportedMaterial
        {
            Name = "Mat_Body",
            Diffuse = new Color(1, 0, 0, 1),
            Textures = new List<ImportedMaterialTexture>()
        };
        imported.MaterialList.Add(mat);

        // Rigid Mesh on Body
        var mesh = new ImportedMesh
        {
            Path = "Root/Body",
            hasNormal = true,
            hasUV = new[] { true, false },
            hasTangent = false,
            hasColor = true,
            VertexList = new List<ImportedVertex>
            {
                new()
                {
                    Vertex = new Vector3(0, 0, 0),
                    Normal = new Vector3(0, 1, 0),
                    UV = new[] { new[] { 0f, 0f } },
                    Color = new Color(1, 1, 1, 1)
                },
                new()
                {
                    Vertex = new Vector3(1, 0, 0),
                    Normal = new Vector3(0, 1, 0),
                    UV = new[] { new[] { 1f, 0f } },
                    Color = new Color(1, 1, 1, 1)
                },
                new()
                {
                    Vertex = new Vector3(0, 1, 0),
                    Normal = new Vector3(0, 1, 0),
                    UV = new[] { new[] { 0f, 1f } },
                    Color = new Color(1, 1, 1, 1)
                }
            },
            SubmeshList = new List<ImportedSubmesh>
            {
                new()
                {
                    Material = "Mat_Body",
                    FaceList = new List<ImportedFace>
                    {
                        new() { VertexIndices = new[] { 0, 1, 2 } }
                    }
                }
            }
        };
        imported.MeshList.Add(mesh);

        // Animation clip
        var anim = new ImportedKeyframedAnimation
        {
            Name = "Walk",
            SampleRate = 30f,
            TrackList = new List<ImportedAnimationKeyframedTrack>()
        };
        var track = new ImportedAnimationKeyframedTrack
        {
            Path = "Root/Body",
            Translations = new List<ImportedKeyframe<Vector3>>
            {
                new(0f, new Vector3(0, 1, 0)),
                new(1f, new Vector3(0, 2, 0))
            },
            RotationQuats = new List<ImportedKeyframe<Quaternion>>
            {
                new(0f, new Quaternion(0, 0, 0, 1)),
                new(1f, new Quaternion(0, 0.7071f, 0, 0.7071f))
            },
            Scalings = new List<ImportedKeyframe<Vector3>>
            {
                new(0f, Vector3.One),
                new(1f, new Vector3(1.2f, 1.2f, 1.2f))
            }
        };
        anim.TrackList.Add(track);
        imported.AnimationList.Add(anim);

        return imported;
    }

    [Fact]
    public void ExportGltf_ExportsValidGlbFile()
    {
        var imported = CreateSampleModel();
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_model_{Guid.NewGuid():N}.glb");

        try
        {
            var settings = new Gltf.Settings
            {
                ExportFormat = Gltf.Format.Glb,
                ScaleFactor = 1.0f,
                ExportAnimations = true,
                ExportSkins = true
            };

            ModelExporter.ExportGltf(tempFile, imported, settings);

            Assert.True(File.Exists(tempFile));
            var fileBytes = File.ReadAllBytes(tempFile);
            Assert.True(fileBytes.Length > 20);

            // Verify GLB magic: 0x46546C67 ("glTF")
            Assert.Equal((byte)'g', fileBytes[0]);
            Assert.Equal((byte)'l', fileBytes[1]);
            Assert.Equal((byte)'T', fileBytes[2]);
            Assert.Equal((byte)'F', fileBytes[3]);

            // Validate with SharpGLTF schema reader
            var model = ModelRoot.Load(tempFile);
            Assert.NotNull(model);
            Assert.NotEmpty(model.LogicalScenes);
            Assert.NotEmpty(model.LogicalNodes);
            Assert.NotEmpty(model.LogicalMeshes);
            Assert.NotEmpty(model.LogicalAnimations);
            Assert.Equal("Walk", model.LogicalAnimations[0].Name);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void ExportGltf_ExportsValidGltfJsonFile()
    {
        var imported = CreateSampleModel();
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_model_{Guid.NewGuid():N}.gltf");

        try
        {
            var settings = new Gltf.Settings
            {
                ExportFormat = Gltf.Format.Gltf,
                ScaleFactor = 1.0f,
                ExportAnimations = true,
                ExportSkins = true
            };

            ModelExporter.ExportGltf(tempFile, imported, settings);

            Assert.True(File.Exists(tempFile));
            var json = File.ReadAllText(tempFile);
            Assert.Contains("\"asset\"", json);
            Assert.Contains("\"version\": \"2.0\"", json);

            // Validate with SharpGLTF schema reader
            var model = ModelRoot.Load(tempFile);
            Assert.NotNull(model);
            Assert.NotEmpty(model.LogicalScenes);
            Assert.NotEmpty(model.LogicalMeshes);
            Assert.NotEmpty(model.LogicalAnimations);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
            var binFile = Path.ChangeExtension(tempFile, ".bin");
            if (File.Exists(binFile))
            {
                File.Delete(binFile);
            }
        }
    }

    [Fact]
    public void ExportGltf_SkinnedMesh_GeneratesJointsAndWeights()
    {
        var imported = new TestImported();
        var root = new ImportedFrame { Name = "Armature" };
        var bone1 = new ImportedFrame { Name = "Bone1", LocalPosition = Vector3.Zero };
        var bone2 = new ImportedFrame { Name = "Bone2", LocalPosition = new Vector3(0, 1, 0) };
        root.AddChild(bone1);
        root.AddChild(bone2);
        imported.RootFrame = root;

        var skinnedMesh = new ImportedMesh
        {
            Path = "Armature",
            hasNormal = true,
            hasUV = new[] { true, false },
            BoneList = new List<ImportedBone>
            {
                new() { Path = "Armature/Bone1", Matrix = Matrix4x4.Scale(Vector3.One) },
                new() { Path = "Armature/Bone2", Matrix = Matrix4x4.Scale(Vector3.One) }
            },
            VertexList = new List<ImportedVertex>
            {
                new()
                {
                    Vertex = new Vector3(0, 0, 0),
                    Normal = new Vector3(0, 1, 0),
                    UV = new[] { new[] { 0f, 0f } },
                    BoneIndices = new[] { 0, 1, 0, 0 },
                    Weights = new[] { 0.8f, 0.2f, 0f, 0f }
                },
                new()
                {
                    Vertex = new Vector3(1, 0, 0),
                    Normal = new Vector3(0, 1, 0),
                    UV = new[] { new[] { 1f, 0f } },
                    BoneIndices = new[] { 0, 1, 0, 0 },
                    Weights = new[] { 0.5f, 0.5f, 0f, 0f }
                },
                new()
                {
                    Vertex = new Vector3(0, 1, 0),
                    Normal = new Vector3(0, 1, 0),
                    UV = new[] { new[] { 0f, 1f } },
                    BoneIndices = new[] { 1, 0, 0, 0 },
                    Weights = new[] { 1.0f, 0f, 0f, 0f }
                }
            },
            SubmeshList = new List<ImportedSubmesh>
            {
                new()
                {
                    FaceList = new List<ImportedFace>
                    {
                        new() { VertexIndices = new[] { 0, 1, 2 } }
                    }
                }
            }
        };
        imported.MeshList.Add(skinnedMesh);

        var tempFile = Path.Combine(Path.GetTempPath(), $"skinned_{Guid.NewGuid():N}.glb");
        try
        {
            var settings = new Gltf.Settings { ExportFormat = Gltf.Format.Glb, ExportSkins = true };
            ModelExporter.ExportGltf(tempFile, imported, settings);

            Assert.True(File.Exists(tempFile));
            var model = ModelRoot.Load(tempFile);
            Assert.NotEmpty(model.LogicalSkins);
            Assert.Equal(2, model.LogicalSkins[0].JointsCount);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
