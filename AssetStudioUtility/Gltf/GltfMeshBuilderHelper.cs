using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using SysVector2 = System.Numerics.Vector2;
using SysVector3 = System.Numerics.Vector3;
using SysVector4 = System.Numerics.Vector4;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Transforms;

namespace AssetStudio
{
    [Flags]
    internal enum GltfMeshFlags
    {
        Position = 0,
        Normal = 1 << 0,
        Tangent = 1 << 1,
        Color = 1 << 2,
        Texture1 = 1 << 3,
        Texture2 = 1 << 4,
        Joints4 = 1 << 5,
    }

    internal static class GltfMeshBuilderHelper
    {
        public static IMeshBuilder<MaterialBuilder> BuildMesh(
            ImportedMesh iMesh,
            IReadOnlyDictionary<string, MaterialBuilder> materialMap,
            MaterialBuilder defaultMaterial,
            float scaleFactor,
            bool exportSkins)
        {
            var flags = GltfMeshFlags.Position;
            if (iMesh.hasNormal) flags |= GltfMeshFlags.Normal;
            if (iMesh.hasTangent) flags |= GltfMeshFlags.Tangent;
            if (iMesh.hasColor) flags |= GltfMeshFlags.Color;
            if (iMesh.hasUV != null && iMesh.hasUV.Length > 0 && iMesh.hasUV[0])
            {
                if (iMesh.hasUV.Length > 1 && iMesh.hasUV[1])
                {
                    flags |= GltfMeshFlags.Texture2;
                }
                else
                {
                    flags |= GltfMeshFlags.Texture1;
                }
            }
            if (exportSkins && iMesh.BoneList != null && iMesh.BoneList.Count > 0)
            {
                flags |= GltfMeshFlags.Joints4;
            }

            bool hasTan = flags.HasFlag(GltfMeshFlags.Tangent) && flags.HasFlag(GltfMeshFlags.Normal);
            bool hasNorm = flags.HasFlag(GltfMeshFlags.Normal);
            bool hasJoints = flags.HasFlag(GltfMeshFlags.Joints4);
            bool hasColor = flags.HasFlag(GltfMeshFlags.Color);
            bool hasUV2 = flags.HasFlag(GltfMeshFlags.Texture2);
            bool hasUV1 = flags.HasFlag(GltfMeshFlags.Texture1) || hasUV2;

            if (hasTan)
            {
                if (hasJoints)
                {
                    if (hasColor && hasUV2) return BuildMesh<VertexPositionNormalTangent, VertexColor1Texture2, VertexJoints4>(iMesh, materialMap, defaultMaterial, scaleFactor);
                    if (hasColor && hasUV1) return BuildMesh<VertexPositionNormalTangent, VertexColor1Texture1, VertexJoints4>(iMesh, materialMap, defaultMaterial, scaleFactor);
                    if (hasColor) return BuildMesh<VertexPositionNormalTangent, VertexColor1, VertexJoints4>(iMesh, materialMap, defaultMaterial, scaleFactor);
                    if (hasUV2) return BuildMesh<VertexPositionNormalTangent, VertexTexture2, VertexJoints4>(iMesh, materialMap, defaultMaterial, scaleFactor);
                    if (hasUV1) return BuildMesh<VertexPositionNormalTangent, VertexTexture1, VertexJoints4>(iMesh, materialMap, defaultMaterial, scaleFactor);
                    return BuildMesh<VertexPositionNormalTangent, VertexEmpty, VertexJoints4>(iMesh, materialMap, defaultMaterial, scaleFactor);
                }
                else
                {
                    if (hasColor && hasUV2) return BuildMesh<VertexPositionNormalTangent, VertexColor1Texture2, VertexEmpty>(iMesh, materialMap, defaultMaterial, scaleFactor);
                    if (hasColor && hasUV1) return BuildMesh<VertexPositionNormalTangent, VertexColor1Texture1, VertexEmpty>(iMesh, materialMap, defaultMaterial, scaleFactor);
                    if (hasColor) return BuildMesh<VertexPositionNormalTangent, VertexColor1, VertexEmpty>(iMesh, materialMap, defaultMaterial, scaleFactor);
                    if (hasUV2) return BuildMesh<VertexPositionNormalTangent, VertexTexture2, VertexEmpty>(iMesh, materialMap, defaultMaterial, scaleFactor);
                    if (hasUV1) return BuildMesh<VertexPositionNormalTangent, VertexTexture1, VertexEmpty>(iMesh, materialMap, defaultMaterial, scaleFactor);
                    return BuildMesh<VertexPositionNormalTangent, VertexEmpty, VertexEmpty>(iMesh, materialMap, defaultMaterial, scaleFactor);
                }
            }
            else if (hasNorm)
            {
                if (hasJoints)
                {
                    if (hasColor && hasUV2) return BuildMesh<VertexPositionNormal, VertexColor1Texture2, VertexJoints4>(iMesh, materialMap, defaultMaterial, scaleFactor);
                    if (hasColor && hasUV1) return BuildMesh<VertexPositionNormal, VertexColor1Texture1, VertexJoints4>(iMesh, materialMap, defaultMaterial, scaleFactor);
                    if (hasColor) return BuildMesh<VertexPositionNormal, VertexColor1, VertexJoints4>(iMesh, materialMap, defaultMaterial, scaleFactor);
                    if (hasUV2) return BuildMesh<VertexPositionNormal, VertexTexture2, VertexJoints4>(iMesh, materialMap, defaultMaterial, scaleFactor);
                    if (hasUV1) return BuildMesh<VertexPositionNormal, VertexTexture1, VertexJoints4>(iMesh, materialMap, defaultMaterial, scaleFactor);
                    return BuildMesh<VertexPositionNormal, VertexEmpty, VertexJoints4>(iMesh, materialMap, defaultMaterial, scaleFactor);
                }
                else
                {
                    if (hasColor && hasUV2) return BuildMesh<VertexPositionNormal, VertexColor1Texture2, VertexEmpty>(iMesh, materialMap, defaultMaterial, scaleFactor);
                    if (hasColor && hasUV1) return BuildMesh<VertexPositionNormal, VertexColor1Texture1, VertexEmpty>(iMesh, materialMap, defaultMaterial, scaleFactor);
                    if (hasColor) return BuildMesh<VertexPositionNormal, VertexColor1, VertexEmpty>(iMesh, materialMap, defaultMaterial, scaleFactor);
                    if (hasUV2) return BuildMesh<VertexPositionNormal, VertexTexture2, VertexEmpty>(iMesh, materialMap, defaultMaterial, scaleFactor);
                    if (hasUV1) return BuildMesh<VertexPositionNormal, VertexTexture1, VertexEmpty>(iMesh, materialMap, defaultMaterial, scaleFactor);
                    return BuildMesh<VertexPositionNormal, VertexEmpty, VertexEmpty>(iMesh, materialMap, defaultMaterial, scaleFactor);
                }
            }
            else
            {
                if (hasJoints)
                {
                    if (hasColor && hasUV2) return BuildMesh<VertexPosition, VertexColor1Texture2, VertexJoints4>(iMesh, materialMap, defaultMaterial, scaleFactor);
                    if (hasColor && hasUV1) return BuildMesh<VertexPosition, VertexColor1Texture1, VertexJoints4>(iMesh, materialMap, defaultMaterial, scaleFactor);
                    if (hasColor) return BuildMesh<VertexPosition, VertexColor1, VertexJoints4>(iMesh, materialMap, defaultMaterial, scaleFactor);
                    if (hasUV2) return BuildMesh<VertexPosition, VertexTexture2, VertexJoints4>(iMesh, materialMap, defaultMaterial, scaleFactor);
                    if (hasUV1) return BuildMesh<VertexPosition, VertexTexture1, VertexJoints4>(iMesh, materialMap, defaultMaterial, scaleFactor);
                    return BuildMesh<VertexPosition, VertexEmpty, VertexJoints4>(iMesh, materialMap, defaultMaterial, scaleFactor);
                }
                else
                {
                    if (hasColor && hasUV2) return BuildMesh<VertexPosition, VertexColor1Texture2, VertexEmpty>(iMesh, materialMap, defaultMaterial, scaleFactor);
                    if (hasColor && hasUV1) return BuildMesh<VertexPosition, VertexColor1Texture1, VertexEmpty>(iMesh, materialMap, defaultMaterial, scaleFactor);
                    if (hasColor) return BuildMesh<VertexPosition, VertexColor1, VertexEmpty>(iMesh, materialMap, defaultMaterial, scaleFactor);
                    if (hasUV2) return BuildMesh<VertexPosition, VertexTexture2, VertexEmpty>(iMesh, materialMap, defaultMaterial, scaleFactor);
                    if (hasUV1) return BuildMesh<VertexPosition, VertexTexture1, VertexEmpty>(iMesh, materialMap, defaultMaterial, scaleFactor);
                    return BuildMesh<VertexPosition, VertexEmpty, VertexEmpty>(iMesh, materialMap, defaultMaterial, scaleFactor);
                }
            }
        }

        private static IMeshBuilder<MaterialBuilder> BuildMesh<TvG, TvM, TvS>(
            ImportedMesh iMesh,
            IReadOnlyDictionary<string, MaterialBuilder> materialMap,
            MaterialBuilder defaultMaterial,
            float scaleFactor)
            where TvG : unmanaged, IVertexGeometry
            where TvM : unmanaged, IVertexMaterial
            where TvS : unmanaged, IVertexSkinning
        {
            var meshBuilder = VertexBuilder<TvG, TvM, TvS>.CreateCompatibleMesh(iMesh.Path ?? "Mesh");

            int vertexCount = iMesh.VertexList != null ? iMesh.VertexList.Count : 0;
            if (vertexCount == 0 || iMesh.SubmeshList == null)
            {
                return meshBuilder;
            }

            // Pre-convert each vertex once to avoid redundant conversions across shared triangle faces
            var convertedVertices = new VertexBuilder<TvG, TvM, TvS>[vertexCount];
            for (int v = 0; v < vertexCount; v++)
            {
                convertedVertices[v] = GetVertex<TvG, TvM, TvS>(iMesh.VertexList[v], scaleFactor);
            }

            foreach (var submesh in iMesh.SubmeshList)
            {
                MaterialBuilder mat = defaultMaterial;
                if (!string.IsNullOrEmpty(submesh.Material) && materialMap.TryGetValue(submesh.Material, out var foundMat))
                {
                    mat = foundMat;
                }

                var prim = meshBuilder.UsePrimitive(mat, 3);
                if (submesh.FaceList == null) continue;

                foreach (var face in submesh.FaceList)
                {
                    if (face.VertexIndices == null || face.VertexIndices.Length < 3) continue;

                    int idx0 = submesh.BaseVertex + face.VertexIndices[0];
                    int idx1 = submesh.BaseVertex + face.VertexIndices[1];
                    int idx2 = submesh.BaseVertex + face.VertexIndices[2];

                    if ((uint)idx0 >= (uint)vertexCount ||
                        (uint)idx1 >= (uint)vertexCount ||
                        (uint)idx2 >= (uint)vertexCount)
                    {
                        continue;
                    }

                    prim.AddTriangle(convertedVertices[idx0], convertedVertices[idx1], convertedVertices[idx2]);
                }
            }

            return meshBuilder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static VertexBuilder<TvG, TvM, TvS> GetVertex<TvG, TvM, TvS>(ImportedVertex v, float scaleFactor)
            where TvG : unmanaged, IVertexGeometry
            where TvM : unmanaged, IVertexMaterial
            where TvS : unmanaged, IVertexSkinning
        {
            var pos = new SysVector3(v.Vertex.X * scaleFactor, v.Vertex.Y * scaleFactor, v.Vertex.Z * scaleFactor);

            TvG geometry;
            if (typeof(TvG) == typeof(VertexPositionNormalTangent))
            {
                var norm = new SysVector3(v.Normal.X, v.Normal.Y, v.Normal.Z);
                var tan = new SysVector4(v.Tangent.X, v.Tangent.Y, v.Tangent.Z, -v.Tangent.W);
                var g = new VertexPositionNormalTangent(pos, norm, tan);
                geometry = Unsafe.As<VertexPositionNormalTangent, TvG>(ref g);
            }
            else if (typeof(TvG) == typeof(VertexPositionNormal))
            {
                var norm = new SysVector3(v.Normal.X, v.Normal.Y, v.Normal.Z);
                var g = new VertexPositionNormal(pos, norm);
                geometry = Unsafe.As<VertexPositionNormal, TvG>(ref g);
            }
            else
            {
                var g = new VertexPosition(pos);
                geometry = Unsafe.As<VertexPosition, TvG>(ref g);
            }

            TvM material;
            var uvList = v.UV;
            if (typeof(TvM) == typeof(VertexTexture1))
            {
                var uv0 = uvList != null && uvList.Length > 0 && uvList[0] != null && uvList[0].Length >= 2
                    ? new SysVector2(uvList[0][0], 1.0f - uvList[0][1])
                    : SysVector2.Zero;
                var m = new VertexTexture1(uv0);
                material = Unsafe.As<VertexTexture1, TvM>(ref m);
            }
            else if (typeof(TvM) == typeof(VertexTexture2))
            {
                var uv0 = uvList != null && uvList.Length > 0 && uvList[0] != null && uvList[0].Length >= 2
                    ? new SysVector2(uvList[0][0], 1.0f - uvList[0][1])
                    : SysVector2.Zero;
                var uv1 = uvList != null && uvList.Length > 1 && uvList[1] != null && uvList[1].Length >= 2
                    ? new SysVector2(uvList[1][0], 1.0f - uvList[1][1])
                    : SysVector2.Zero;
                var m = new VertexTexture2(uv0, uv1);
                material = Unsafe.As<VertexTexture2, TvM>(ref m);
            }
            else if (typeof(TvM) == typeof(VertexColor1))
            {
                var col = new SysVector4(v.Color.R, v.Color.G, v.Color.B, v.Color.A);
                var m = new VertexColor1(col);
                material = Unsafe.As<VertexColor1, TvM>(ref m);
            }
            else if (typeof(TvM) == typeof(VertexColor1Texture1))
            {
                var col = new SysVector4(v.Color.R, v.Color.G, v.Color.B, v.Color.A);
                var uv0 = uvList != null && uvList.Length > 0 && uvList[0] != null && uvList[0].Length >= 2
                    ? new SysVector2(uvList[0][0], 1.0f - uvList[0][1])
                    : SysVector2.Zero;
                var m = new VertexColor1Texture1(col, uv0);
                material = Unsafe.As<VertexColor1Texture1, TvM>(ref m);
            }
            else if (typeof(TvM) == typeof(VertexColor1Texture2))
            {
                var col = new SysVector4(v.Color.R, v.Color.G, v.Color.B, v.Color.A);
                var uv0 = uvList != null && uvList.Length > 0 && uvList[0] != null && uvList[0].Length >= 2
                    ? new SysVector2(uvList[0][0], 1.0f - uvList[0][1])
                    : SysVector2.Zero;
                var uv1 = uvList != null && uvList.Length > 1 && uvList[1] != null && uvList[1].Length >= 2
                    ? new SysVector2(uvList[1][0], 1.0f - uvList[1][1])
                    : SysVector2.Zero;
                var m = new VertexColor1Texture2(col, uv0, uv1);
                material = Unsafe.As<VertexColor1Texture2, TvM>(ref m);
            }
            else
            {
                var m = new VertexEmpty();
                material = Unsafe.As<VertexEmpty, TvM>(ref m);
            }

            TvS skinning;
            if (typeof(TvS) == typeof(VertexJoints4))
            {
                var s = new VertexJoints4();
                var bIdx = v.BoneIndices;
                var bWgt = v.Weights;
                if (bIdx != null && bWgt != null && bWgt.Length > 0)
                {
                    var indices = new SysVector4(
                        bIdx.Length > 0 ? bIdx[0] : 0,
                        bIdx.Length > 1 ? bIdx[1] : 0,
                        bIdx.Length > 2 ? bIdx[2] : 0,
                        bIdx.Length > 3 ? bIdx[3] : 0);
                    var weights = new SysVector4(
                        bWgt.Length > 0 ? bWgt[0] : 0,
                        bWgt.Length > 1 ? bWgt[1] : 0,
                        bWgt.Length > 2 ? bWgt[2] : 0,
                        bWgt.Length > 3 ? bWgt[3] : 0);
                    float sum = weights.X + weights.Y + weights.Z + weights.W;
                    if (sum > 0f)
                    {
                        weights /= sum;
                    }
                    var sw = SparseWeight8.Create(indices, weights);
                    s = new VertexJoints4(sw);
                }
                skinning = Unsafe.As<VertexJoints4, TvS>(ref s);
            }
            else
            {
                var s = new VertexEmpty();
                skinning = Unsafe.As<VertexEmpty, TvS>(ref s);
            }

            return new VertexBuilder<TvG, TvM, TvS>(geometry, material, skinning);
        }
    }
}
