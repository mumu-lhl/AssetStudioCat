#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SysVector2 = System.Numerics.Vector2;
using SysVector3 = System.Numerics.Vector3;
using SysVector4 = System.Numerics.Vector4;
using SysQuaternion = System.Numerics.Quaternion;
using SysMatrix4x4 = System.Numerics.Matrix4x4;
using SharpGLTF.Geometry;
using SharpGLTF.Materials;
using SharpGLTF.Memory;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;
using SharpGLTF.Transforms;

namespace AssetStudio
{
    public static class GltfExporter
    {
        public static void Export(string path, IImported imported, Gltf.Settings? settings = null)
        {
            if (imported == null)
            {
                throw new ArgumentNullException(nameof(imported));
            }

            settings ??= new Gltf.Settings();

            var extension = Path.GetExtension(path);
            if (string.IsNullOrEmpty(extension))
            {
                path += Gltf.Settings.GetFileExtension(settings.ExportFormat);
            }
            else if (string.Equals(extension, ".gltf", StringComparison.OrdinalIgnoreCase))
            {
                settings.ExportFormat = Gltf.Format.Gltf;
            }
            else if (string.Equals(extension, ".glb", StringComparison.OrdinalIgnoreCase))
            {
                settings.ExportFormat = Gltf.Format.Glb;
            }

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var sceneBuilder = BuildScene(imported, settings);
            var sceneSettings = new SceneBuilderSchema2Settings
            {
                CompactVertexWeights = true
            };
            var modelRoot = sceneBuilder.ToGltf2(sceneSettings);

            try
            {
                if (settings.ExportFormat == Gltf.Format.Glb)
                {
                    modelRoot.SaveGLB(path);
                }
                else
                {
                    modelRoot.SaveGLTF(path);
                }
            }
            catch (InvalidOperationException ex) when (ex.Message?.Contains("2Gb", StringComparison.OrdinalIgnoreCase) == true)
            {
                throw new InvalidOperationException("Model was too large to export as glTF/GLB (buffer size exceeded 2GB limit).", ex);
            }
            catch (ArgumentException ex) when (ex.Message?.Contains("buffer", StringComparison.OrdinalIgnoreCase) == true)
            {
                throw new InvalidOperationException("Model was too large to export as glTF/GLB.", ex);
            }
        }

        public static SceneBuilder BuildScene(IImported imported, Gltf.Settings settings)
        {
            var sceneBuilder = new SceneBuilder();
            var frameToNode = new Dictionary<ImportedFrame, NodeBuilder>();
            var pathToNode = new Dictionary<string, NodeBuilder>(StringComparer.OrdinalIgnoreCase);

            // 1. Build hierarchy of nodes
            if (imported.RootFrame != null)
            {
                void ProcessFrame(ImportedFrame frame, NodeBuilder? parentNode)
                {
                    var nodeName = string.IsNullOrWhiteSpace(frame.Name) ? "Node" : frame.Name;
                    var node = parentNode is null ? new NodeBuilder(nodeName) : parentNode.CreateNode(nodeName);

                    var pos = new SysVector3(
                        frame.LocalPosition.X * settings.ScaleFactor,
                        frame.LocalPosition.Y * settings.ScaleFactor,
                        frame.LocalPosition.Z * settings.ScaleFactor
                    );
                    var rot = (frame.LocalRotationQ.X == 0 && frame.LocalRotationQ.Y == 0 && frame.LocalRotationQ.Z == 0 && frame.LocalRotationQ.W == 0)
                        ? SysQuaternion.Identity
                        : new SysQuaternion(frame.LocalRotationQ.X, frame.LocalRotationQ.Y, frame.LocalRotationQ.Z, frame.LocalRotationQ.W);
                    var scale = new SysVector3(frame.LocalScale.X, frame.LocalScale.Y, frame.LocalScale.Z);

                    node.LocalTransform = new AffineTransform(scale, rot, pos);

                    if (parentNode is null)
                    {
                        sceneBuilder.AddNode(node);
                    }

                    frameToNode[frame] = node;
                    if (!string.IsNullOrEmpty(frame.Path))
                    {
                        pathToNode[frame.Path] = node;
                    }

                    for (int i = 0; i < frame.Count; i++)
                    {
                        ProcessFrame(frame[i], node);
                    }
                }

                ProcessFrame(imported.RootFrame, null);
            }

            // 2. Prepare materials and textures
            var textureLookup = new Dictionary<string, ImportedTexture>(StringComparer.OrdinalIgnoreCase);
            if (imported.TextureList != null)
            {
                foreach (var tex in imported.TextureList)
                {
                    if (!string.IsNullOrEmpty(tex.Name))
                    {
                        textureLookup.TryAdd(tex.Name, tex);
                    }
                }
            }

            var imageCache = new Dictionary<string, MemoryImage>(StringComparer.OrdinalIgnoreCase);
            MemoryImage? GetImage(string textureName)
            {
                if (string.IsNullOrEmpty(textureName)) return null;
                if (imageCache.TryGetValue(textureName, out var cached)) return cached;

                if (textureLookup.TryGetValue(textureName, out var tex) && tex.Data != null && tex.Data.Length > 0)
                {
                    var memImage = new MemoryImage(tex.Data);
                    imageCache[textureName] = memImage;
                    return memImage;
                }
                return null;
            }

            var materialMap = new Dictionary<string, MaterialBuilder>(StringComparer.OrdinalIgnoreCase);
            var defaultMaterial = new MaterialBuilder("DefaultMaterial")
                .WithBaseColor(new SysVector4(0.8f, 0.8f, 0.8f, 1.0f));
            defaultMaterial.DoubleSided = false;

            if (imported.MaterialList != null)
            {
                foreach (var mat in imported.MaterialList)
                {
                    var matBuilder = new MaterialBuilder(mat.Name ?? "Material");
                    matBuilder.DoubleSided = false;

                    var baseColor = new SysVector4(
                        mat.Diffuse.R,
                        mat.Diffuse.G,
                        mat.Diffuse.B,
                        mat.Diffuse.A <= 0f ? 1.0f : mat.Diffuse.A
                    );

                    bool hasBaseTexture = false;
                    if (mat.Textures != null && mat.Textures.Count > 0)
                    {
                        foreach (var matTex in mat.Textures)
                        {
                            var img = GetImage(matTex.Name);
                            if (img.HasValue && !img.Value.IsEmpty)
                            {
                                if (matTex.Dest == 0) // Diffuse / Main
                                {
                                    matBuilder.WithBaseColor(img.Value);
                                    hasBaseTexture = true;
                                }
                                else if (matTex.Dest == 1 || matTex.Dest == 3) // Normal / Bump
                                {
                                    matBuilder.WithNormal(img.Value);
                                }
                            }
                        }
                    }

                    if (!hasBaseTexture)
                    {
                        matBuilder.WithBaseColor(baseColor);
                    }

                    if (mat.Name != null)
                    {
                        materialMap[mat.Name] = matBuilder;
                    }
                }
            }

            // 3. Build and attach meshes
            if (imported.MeshList != null)
            {
                foreach (var mesh in imported.MeshList)
                {
                    if (mesh.VertexList == null || mesh.VertexList.Count == 0 || mesh.SubmeshList == null || mesh.SubmeshList.Count == 0)
                    {
                        continue;
                    }

                    var meshBuilder = GltfMeshBuilderHelper.BuildMesh(mesh, materialMap, defaultMaterial, settings.ScaleFactor, settings.ExportSkins);

                    bool isSkinned = settings.ExportSkins && mesh.BoneList != null && mesh.BoneList.Count > 0;
                    if (isSkinned && mesh.BoneList != null)
                    {
                        var joints = new (NodeBuilder Joint, SysMatrix4x4 InverseBindMatrix)[mesh.BoneList.Count];
                        for (int b = 0; b < mesh.BoneList.Count; b++)
                        {
                            var bone = mesh.BoneList[b];
                            NodeBuilder? jointNode = null;
                            if (!string.IsNullOrEmpty(bone.Path))
                            {
                                pathToNode.TryGetValue(bone.Path, out jointNode);
                                if (jointNode == null && imported.RootFrame != null)
                                {
                                    var foundFrame = imported.RootFrame.FindFrameByPath(bone.Path);
                                    if (foundFrame != null)
                                    {
                                        frameToNode.TryGetValue(foundFrame, out jointNode);
                                    }
                                }
                            }

                            jointNode ??= pathToNode.Values.FirstOrDefault() ?? new NodeBuilder(bone.Path ?? $"Bone_{b}");

                            var ibm = new SysMatrix4x4(
                                bone.Matrix.M00, bone.Matrix.M01, bone.Matrix.M02, bone.Matrix.M03,
                                bone.Matrix.M10, bone.Matrix.M11, bone.Matrix.M12, bone.Matrix.M13,
                                bone.Matrix.M20, bone.Matrix.M21, bone.Matrix.M22, bone.Matrix.M23,
                                bone.Matrix.M30 * settings.ScaleFactor,
                                bone.Matrix.M31 * settings.ScaleFactor,
                                bone.Matrix.M32 * settings.ScaleFactor,
                                bone.Matrix.M33
                            );
                            joints[b] = (jointNode, ibm);
                        }

                        sceneBuilder.AddSkinnedMesh(meshBuilder, joints);
                    }
                    else
                    {
                        NodeBuilder? meshNode = null;
                        if (!string.IsNullOrEmpty(mesh.Path))
                        {
                            pathToNode.TryGetValue(mesh.Path, out meshNode);
                            if (meshNode == null && imported.RootFrame != null)
                            {
                                var foundFrame = imported.RootFrame.FindFrameByPath(mesh.Path);
                                if (foundFrame != null)
                                {
                                    frameToNode.TryGetValue(foundFrame, out meshNode);
                                }
                            }
                        }

                        meshNode ??= pathToNode.Values.FirstOrDefault() ?? new NodeBuilder(mesh.Path ?? "Mesh");
                        sceneBuilder.AddRigidMesh(meshBuilder, meshNode);
                    }
                }
            }

            // 4. Build animations
            if (settings.ExportAnimations && imported.AnimationList != null && imported.AnimationList.Count > 0)
            {
                foreach (var anim in imported.AnimationList)
                {
                    var clipName = string.IsNullOrWhiteSpace(anim.Name) ? "Default" : anim.Name;
                    if (anim.TrackList == null) continue;

                    foreach (var track in anim.TrackList)
                    {
                        if (string.IsNullOrEmpty(track.Path)) continue;

                        NodeBuilder? trackNode = null;
                        if (!pathToNode.TryGetValue(track.Path, out trackNode) && imported.RootFrame != null)
                        {
                            var foundFrame = imported.RootFrame.FindFrameByPath(track.Path);
                            if (foundFrame != null)
                            {
                                frameToNode.TryGetValue(foundFrame, out trackNode);
                            }
                        }

                        if (trackNode == null) continue;

                        // Translation curve
                        if (track.Translations != null && track.Translations.Count > 0)
                        {
                            var curve = trackNode.UseTranslation(clipName);
                            foreach (var kf in track.Translations)
                            {
                                curve.WithPoint(
                                    kf.time,
                                    new SysVector3(kf.value.X * settings.ScaleFactor, kf.value.Y * settings.ScaleFactor, kf.value.Z * settings.ScaleFactor)
                                );
                            }
                        }

                        // Rotation curve
                        if (track.RotationQuats != null && track.RotationQuats.Count > 0)
                        {
                            var curve = trackNode.UseRotation(clipName);
                            foreach (var kf in track.RotationQuats)
                            {
                                curve.WithPoint(
                                    kf.time,
                                    new SysQuaternion(kf.value.X, kf.value.Y, kf.value.Z, kf.value.W)
                                );
                            }
                        }
                        else if (track.Rotations != null && track.Rotations.Count > 0)
                        {
                            var curve = trackNode.UseRotation(clipName);
                            foreach (var kf in track.Rotations)
                            {
                                var q = EulerToQuaternion(kf.value);
                                curve.WithPoint(kf.time, q);
                            }
                        }

                        // Scaling curve
                        if (track.Scalings != null && track.Scalings.Count > 0)
                        {
                            var curve = trackNode.UseScale(clipName);
                            foreach (var kf in track.Scalings)
                            {
                                curve.WithPoint(
                                    kf.time,
                                    new SysVector3(kf.value.X, kf.value.Y, kf.value.Z)
                                );
                            }
                        }
                    }
                }
            }

            return sceneBuilder;
        }

        private static SysQuaternion EulerToQuaternion(Vector3 v)
        {
            try
            {
                var q = Fbx.EulerToQuaternion(v);
                return new SysQuaternion(q.X, q.Y, q.Z, q.W);
            }
            catch
            {
                float deg2rad = (float)(Math.PI / 180.0);
                var qx = SysQuaternion.CreateFromAxisAngle(SysVector3.UnitX, v.X * deg2rad);
                var qy = SysQuaternion.CreateFromAxisAngle(SysVector3.UnitY, v.Y * deg2rad);
                var qz = SysQuaternion.CreateFromAxisAngle(SysVector3.UnitZ, v.Z * deg2rad);
                return qx * qy * qz;
            }
        }
    }
}
