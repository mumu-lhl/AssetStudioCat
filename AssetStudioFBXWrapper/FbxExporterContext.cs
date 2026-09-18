using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace AssetStudio.FbxInterop
{
    internal sealed partial class FbxExporterContext : IDisposable
    {

        private IntPtr _pContext;
        private readonly Dictionary<ImportedFrame, IntPtr> _frameToNode;
        private readonly Dictionary<string, IntPtr> _pathToNode;
        private readonly List<(string Path, IntPtr Node)> _nodeList;
        private readonly Dictionary<string, IntPtr> _createdMaterials;
        private readonly Dictionary<string, IntPtr> _createdTextures;

        public FbxExporterContext()
        {
            _pContext = AsFbxCreateContext();
            _frameToNode = new Dictionary<ImportedFrame, IntPtr>();
            _pathToNode = new Dictionary<string, IntPtr>();
            _nodeList = new List<(string, IntPtr)>();
            _createdMaterials = new Dictionary<string, IntPtr>();
            _createdTextures = new Dictionary<string, IntPtr>();
        }

        ~FbxExporterContext()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public bool IsDisposed { get; private set; }

        private void Dispose(bool disposing)
        {
            IsDisposed = true;

            _frameToNode.Clear();
            _pathToNode.Clear();
            _nodeList.Clear();
            _createdMaterials.Clear();
            _createdTextures.Clear();

            AsFbxDisposeContext(ref _pContext);
        }

        private void EnsureNotDisposed()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(nameof(FbxExporterContext));
            }
        }

        internal void Initialize(string fileName, Fbx.Settings fbxSettings, bool is60Fps)
        {
            EnsureNotDisposed();

            var b = AsFbxInitializeContext(_pContext, fileName, fbxSettings.ScaleFactor, fbxSettings.FbxVersionIndex, fbxSettings.IsAscii, is60Fps, out var errorMessage);

            if (!b)
            {
                var fullMessage = $"Failed to initialize FbxExporter: {errorMessage}";
                throw new ApplicationException(fullMessage);
            }
        }

        internal void SetFramePaths(HashSet<string> framePaths)
        {
            EnsureNotDisposed();

            if (framePaths == null || framePaths.Count == 0)
            {
                return;
            }

            var framePathArray = new string[framePaths.Count];
            framePaths.CopyTo(framePathArray);

            AsFbxSetFramePaths(_pContext, framePathArray);
        }

        internal void ExportScene()
        {
            EnsureNotDisposed();

            AsFbxExportScene(_pContext);
        }

        internal void ExportFrame(List<ImportedMesh> meshList, List<ImportedFrame> meshFrames, ImportedFrame rootFrame)
        {
            var rootNode = AsFbxGetSceneRootNode(_pContext);

            Debug.Assert(rootNode != IntPtr.Zero);

            HashSet<string> meshPathSet = null;
            if (meshList != null && meshList.Count > 0)
            {
                meshPathSet = new HashSet<string>(meshList.Count);
                for (var i = 0; i < meshList.Count; i++)
                {
                    var p = meshList[i].Path;
                    if (p != null)
                    {
                        meshPathSet.Add(p);
                    }
                }
            }

            var nodeStack = new Stack<IntPtr>();
            var frameStack = new Stack<ImportedFrame>();

            nodeStack.Push(rootNode);
            frameStack.Push(rootFrame);

            while (nodeStack.Count > 0)
            {
                var parentNode = nodeStack.Pop();
                var frame = frameStack.Pop();

                var path = frame.Path;
                var childNode = AsFbxExportSingleFrame(_pContext, parentNode, path, frame.Name, frame.LocalPosition, frame.LocalRotation, frame.LocalScale);

                if (meshPathSet != null && meshPathSet.Contains(path))
                {
                    meshFrames.Add(frame);
                }

                _frameToNode[frame] = childNode;
                if (childNode != IntPtr.Zero)
                {
                    _pathToNode[path] = childNode;
                    _nodeList.Add((path, childNode));
                }

                for (var i = frame.Count - 1; i >= 0; i -= 1)
                {
                    nodeStack.Push(childNode);
                    frameStack.Push(frame[i]);
                }
            }
        }

        internal void SetJointsNode(ImportedFrame rootFrame, HashSet<string> bonePaths, bool castToBone, float boneSize)
        {
            if (castToBone)
            {
                for (var i = 0; i < _nodeList.Count; i++)
                {
                    AsFbxSetJointsNode_CastToBone(_pContext, _nodeList[i].Node, boneSize);
                }
            }
            else
            {
                Debug.Assert(bonePaths != null);

                for (var i = 0; i < _nodeList.Count; i++)
                {
                    var item = _nodeList[i];
                    if (bonePaths.Contains(item.Path))
                    {
                        AsFbxSetJointsNode_BoneInPath(_pContext, item.Node, boneSize);
                    }
                    else
                    {
                        AsFbxSetJointsNode_Generic(_pContext, item.Node);
                    }
                }
            }
        }

        internal void PrepareMaterials(int materialCount, int textureCount)
        {
            AsFbxPrepareMaterials(_pContext, materialCount, textureCount);
        }

        internal void ExportMeshFromFrame(ImportedFrame meshFrame, ImportedMesh mesh, Dictionary<string, ImportedMaterial> materialMap, Dictionary<string, ImportedTexture> textureMap, Fbx.Settings fbxSettings)
        {
            if (!_frameToNode.TryGetValue(meshFrame, out var meshNode) || meshNode == IntPtr.Zero || mesh == null)
            {
                return;
            }

            ExportMesh(materialMap, textureMap, meshNode, mesh, fbxSettings);
        }

        private IntPtr ExportTexture(ImportedTexture texture)
        {
            if (texture == null)
            {
                return IntPtr.Zero;
            }

            if (_createdTextures.TryGetValue(texture.Name, out var pTex))
            {
                return pTex;
            }

            pTex = AsFbxCreateTexture(_pContext, texture.Name);

            _createdTextures.Add(texture.Name, pTex);

            if (texture.Data != null)
            {
                File.WriteAllBytes(texture.Name, texture.Data);
            }

            return pTex;
        }

        private void ExportMesh(Dictionary<string, ImportedMaterial> materialMap, Dictionary<string, ImportedTexture> textureMap, IntPtr frameNode, ImportedMesh importedMesh, Fbx.Settings fbxSettings)
        {
            var boneList = importedMesh.BoneList;
            var totalBoneCount = 0;
            var hasBones = false;
            if (fbxSettings.ExportSkins && boneList?.Count > 0)
            {
                totalBoneCount = boneList.Count;
                hasBones = true;
            }

            var pClusterArray = IntPtr.Zero;

            try
            {
                if (hasBones)
                {
                    pClusterArray = AsFbxMeshCreateClusterArray(totalBoneCount);

                    for (var b = 0; b < boneList.Count; b++)
                    {
                        var bone = boneList[b];
                        if (bone.Path != null && _pathToNode.TryGetValue(bone.Path, out var boneNode) && boneNode != IntPtr.Zero)
                        {
                            var cluster = AsFbxMeshCreateCluster(_pContext, boneNode);
                            AsFbxMeshAddCluster(pClusterArray, cluster);
                        }
                        else
                        {
                            AsFbxMeshAddCluster(pClusterArray, IntPtr.Zero);
                        }
                    }
                }

                var mesh = AsFbxMeshCreateMesh(_pContext, frameNode);

                AsFbxMeshInitControlPoints(mesh, importedMesh.VertexList.Count);

                if (importedMesh.hasNormal)
                {
                    AsFbxMeshCreateElementNormal(mesh);
                }

                var activeUvIndices = new List<int>();
                if (importedMesh.hasUV != null)
                {
                    for (var i = 0; i < importedMesh.hasUV.Length; i++)
                    {
                        if (!importedMesh.hasUV[i])
                            continue;

                        if (fbxSettings.ExportAllUvsAsDiffuseMaps)
                        {
                            AsFbxMeshCreateUVMap(mesh, i, 0);
                            activeUvIndices.Add(i);
                        }
                        else if (fbxSettings.UvBindings != null && fbxSettings.UvBindings.TryGetValue(i, out var binding) && binding > 0)
                        {
                            AsFbxMeshCreateUVMap(mesh, i, binding - 1);
                            activeUvIndices.Add(i);
                        }
                    }
                }
                var activeUvArray = activeUvIndices.ToArray();

                if (importedMesh.hasTangent)
                {
                    AsFbxMeshCreateElementTangent(mesh);
                }

                if (importedMesh.hasColor)
                {
                    AsFbxMeshCreateElementVertexColor(mesh);
                }

                AsFbxMeshCreateElementMaterial(mesh);

                if (importedMesh.SubmeshList != null)
                {
                    for (var s = 0; s < importedMesh.SubmeshList.Count; s++)
                    {
                        var meshObj = importedMesh.SubmeshList[s];
                        var materialIndex = 0;
                        ImportedMaterial mat = null;
                        if (meshObj.Material != null)
                        {
                            materialMap?.TryGetValue(meshObj.Material, out mat);
                        }

                        if (mat != null)
                        {
                            if (!_createdMaterials.TryGetValue(mat.Name, out var pMat))
                            {
                                var diffuse = mat.Diffuse;
                                var ambient = mat.Ambient;
                                var emissive = mat.Emissive;
                                var specular = mat.Specular;
                                var reflection = mat.Reflection;

                                pMat = AsFbxCreateMaterial(_pContext, mat.Name, in diffuse, in ambient, in emissive, in specular, in reflection, mat.Shininess, mat.Transparency);

                                _createdMaterials[mat.Name] = pMat;
                            }

                            materialIndex = AsFbxAddMaterialToFrame(frameNode, pMat);

                            var hasTexture = false;

                            if (mat.Textures != null)
                            {
                                for (var t = 0; t < mat.Textures.Count; t++)
                                {
                                    var texture = mat.Textures[t];
                                    ImportedTexture tex = null;
                                    if (texture.Name != null)
                                    {
                                        textureMap?.TryGetValue(texture.Name, out tex);
                                    }
                                    var pTexture = ExportTexture(tex);

                                    if (pTexture != IntPtr.Zero)
                                    {
                                        switch (texture.Dest)
                                        {
                                            case 0:
                                            case 1:
                                            case 2:
                                            case 3:
                                                AsFbxLinkTexture(texture.Dest, pTexture, pMat, texture.Offset.X, texture.Offset.Y, texture.Scale.X, texture.Scale.Y);
                                                hasTexture = true;
                                                break;
                                            default:
                                                break;
                                        }
                                    }
                                }
                            }

                            if (hasTexture)
                            {
                                AsFbxSetFrameShadingModeToTextureShading(frameNode);
                            }
                        }

                        var faceList = meshObj.FaceList;
                        var baseVertex = meshObj.BaseVertex;
                        if (faceList != null)
                        {
                            for (var f = 0; f < faceList.Count; f++)
                            {
                                var face = faceList[f];
                                var vi = face.VertexIndices;
                                AsFbxMeshAddPolygon(mesh, materialIndex, vi[0] + baseVertex, vi[1] + baseVertex, vi[2] + baseVertex);
                            }
                        }
                    }
                }

                var vertexList = importedMesh.VertexList;
                var vertexCount = vertexList.Count;
                var hasNormal = importedMesh.hasNormal;
                var hasTangent = importedMesh.hasTangent;
                var hasColor = importedMesh.hasColor;

                for (var j = 0; j < vertexCount; j += 1)
                {
                    var importedVertex = vertexList[j];

                    var vertex = importedVertex.Vertex;
                    AsFbxMeshSetControlPoint(mesh, j, vertex.X, vertex.Y, vertex.Z);

                    if (hasNormal)
                    {
                        var normal = importedVertex.Normal;
                        AsFbxMeshElementNormalAdd(mesh, 0, normal.X, normal.Y, normal.Z);
                    }

                    for (var uvIdx = 0; uvIdx < activeUvArray.Length; uvIdx++)
                    {
                        var uvIndex = activeUvArray[uvIdx];
                        var uv = importedVertex.UV[uvIndex];
                        AsFbxMeshElementUVAdd(mesh, uvIndex, uv[0], uv[1]);
                    }

                    if (hasTangent)
                    {
                        var tangent = importedVertex.Tangent;
                        AsFbxMeshElementTangentAdd(mesh, 0, tangent.X, tangent.Y, tangent.Z, tangent.W);
                    }

                    if (hasColor)
                    {
                        var color = importedVertex.Color;
                        AsFbxMeshElementVertexColorAdd(mesh, 0, color.R, color.G, color.B, color.A);
                    }

                    if (hasBones && importedVertex.BoneIndices != null && importedVertex.Weights != null)
                    {
                        var boneIndices = importedVertex.BoneIndices;
                        var boneWeights = importedVertex.Weights;

                        for (var k = 0; k < 4; k += 1)
                        {
                            if (boneIndices[k] < totalBoneCount && boneWeights[k] > 0)
                            {
                                AsFbxMeshSetBoneWeight(pClusterArray, boneIndices[k], j, boneWeights[k]);
                            }
                        }
                    }
                }

                if (hasBones)
                {
                    IntPtr pSkinContext = IntPtr.Zero;

                    try
                    {
                        pSkinContext = AsFbxMeshCreateSkinContext(_pContext, frameNode);

                        unsafe
                        {
                            var boneMatrix = stackalloc float[16];

                            for (var j = 0; j < totalBoneCount; j += 1)
                            {
                                if (!FbxClusterArray_HasItemAt(pClusterArray, j))
                                {
                                    continue;
                                }

                                var m = boneList[j].Matrix;

                                CopyMatrix4x4(in m, boneMatrix);

                                AsFbxMeshSkinAddCluster(pSkinContext, pClusterArray, j, boneMatrix);
                            }
                        }

                        AsFbxMeshAddDeformer(pSkinContext, mesh);
                    }
                    finally
                    {
                        AsFbxMeshDisposeSkinContext(ref pSkinContext);
                    }
                }
            }
            finally
            {
                AsFbxMeshDisposeClusterArray(ref pClusterArray);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void CopyMatrix4x4(in Matrix4x4 matrix, float* buffer)
        {
            for (var m = 0; m < 4; m += 1)
            {
                for (var n = 0; n < 4; n += 1)
                {
                    var index = IndexFrom4x4(m, n);
                    buffer[index] = matrix[m, n];
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int IndexFrom4x4(int m, int n)
        {
            return 4 * m + n;
        }

        internal void ExportAnimations(ImportedFrame rootFrame, List<ImportedKeyframedAnimation> animationList, bool eulerFilter, float filterPrecision)
        {
            if (animationList == null || animationList.Count == 0)
            {
                return;
            }

            var pAnimContext = IntPtr.Zero;

            try
            {
                pAnimContext = AsFbxAnimCreateContext(eulerFilter);

                for (int i = 0; i < animationList.Count; i++)
                {
                    var importedAnimation = animationList[i];
                    string takeName;

                    if (importedAnimation.Name != null)
                    {
                        takeName = importedAnimation.Name;
                    }
                    else
                    {
                        takeName = $"Take{i.ToString()}";
                    }

                    AsFbxAnimPrepareStackAndLayer(_pContext, pAnimContext, takeName);

                    ExportKeyframedAnimation(importedAnimation, pAnimContext, filterPrecision);
                }
            }
            finally
            {
                AsFbxAnimDisposeContext(ref pAnimContext);
            }
        }

        private void ExportKeyframedAnimation(ImportedKeyframedAnimation parser, IntPtr pAnimContext, float filterPrecision)
        {
            var trackList = parser.TrackList;
            if (trackList == null)
            {
                return;
            }

            for (var t = 0; t < trackList.Count; t++)
            {
                var track = trackList[t];
                if (track.Path == null || !_pathToNode.TryGetValue(track.Path, out var pNode) || pNode == IntPtr.Zero)
                {
                    continue;
                }

                AsFbxAnimLoadCurves(pNode, pAnimContext);

                AsFbxAnimBeginKeyModify(pAnimContext);

                var scalings = track.Scalings;
                if (scalings != null)
                {
                    for (var i = 0; i < scalings.Count; i++)
                    {
                        var scaling = scalings[i];
                        var value = scaling.value;
                        AsFbxAnimAddScalingKey(pAnimContext, scaling.time, value.X, value.Y, value.Z);
                    }
                }

                var rotations = track.Rotations;
                if (rotations != null)
                {
                    for (var i = 0; i < rotations.Count; i++)
                    {
                        var rotation = rotations[i];
                        var value = rotation.value;
                        AsFbxAnimAddRotationKey(pAnimContext, rotation.time, value.X, value.Y, value.Z);
                    }
                }

                var translations = track.Translations;
                if (translations != null)
                {
                    for (var i = 0; i < translations.Count; i++)
                    {
                        var translation = translations[i];
                        var value = translation.value;
                        AsFbxAnimAddTranslationKey(pAnimContext, translation.time, value.X, value.Y, value.Z);
                    }
                }

                AsFbxAnimEndKeyModify(pAnimContext);

                AsFbxAnimApplyEulerFilter(pAnimContext, filterPrecision);

                var blendShape = track.BlendShape;

                if (blendShape != null)
                {
                    var channelCount = AsFbxAnimGetCurrentBlendShapeChannelCount(pAnimContext, pNode);

                    if (channelCount > 0)
                    {
                        var keyframes = blendShape.Keyframes;
                        for (var channelIndex = 0; channelIndex < channelCount; channelIndex += 1)
                        {
                            if (!AsFbxAnimIsBlendShapeChannelMatch(pAnimContext, channelIndex, blendShape.ChannelName))
                            {
                                continue;
                            }

                            AsFbxAnimBeginBlendShapeAnimCurve(pAnimContext, channelIndex);

                            if (keyframes != null)
                            {
                                for (var k = 0; k < keyframes.Count; k++)
                                {
                                    var keyframe = keyframes[k];
                                    AsFbxAnimAddBlendShapeKeyframe(pAnimContext, keyframe.time, keyframe.value);
                                }
                            }

                            AsFbxAnimEndBlendShapeAnimCurve(pAnimContext);
                        }
                    }
                }
            }
        }

        internal void ExportMorphs(ImportedFrame rootFrame, List<ImportedMorph> morphList)
        {
            if (morphList == null || morphList.Count == 0)
            {
                return;
            }

            for (var m = 0; m < morphList.Count; m++)
            {
                var morph = morphList[m];
                if (morph.Path == null || !_pathToNode.TryGetValue(morph.Path, out var pNode) || pNode == IntPtr.Zero)
                {
                    continue;
                }

                var pMorphContext = IntPtr.Zero;

                try
                {
                    pMorphContext = AsFbxMorphCreateContext();

                    AsFbxMorphInitializeContext(_pContext, pMorphContext, pNode);

                    var channels = morph.Channels;
                    if (channels != null)
                    {
                        for (var c = 0; c < channels.Count; c++)
                        {
                            var channel = channels[c];
                            AsFbxMorphAddBlendShapeChannel(_pContext, pMorphContext, channel.Name);

                            var keyframeList = channel.KeyframeList;
                            if (keyframeList != null)
                            {
                                for (var i = 0; i < keyframeList.Count; i++)
                                {
                                    var keyframe = keyframeList[i];

                                    AsFbxMorphAddBlendShapeChannelShape(_pContext, pMorphContext, keyframe.Weight, i == 0 ? channel.Name : $"{channel.Name}_{i + 1}");

                                    AsFbxMorphCopyBlendShapeControlPoints(pMorphContext);

                                    var vertexList = keyframe.VertexList;
                                    if (vertexList != null)
                                    {
                                        for (var v = 0; v < vertexList.Count; v++)
                                        {
                                            var vertex = vertexList[v];
                                            var vert = vertex.Vertex.Vertex;
                                            AsFbxMorphSetBlendShapeVertex(pMorphContext, vertex.Index, vert.X, vert.Y, vert.Z);
                                        }

                                        if (keyframe.hasNormals)
                                        {
                                            AsFbxMorphCopyBlendShapeControlPointsNormal(pMorphContext);

                                            for (var v = 0; v < vertexList.Count; v++)
                                            {
                                                var vertex = vertexList[v];
                                                var norm = vertex.Vertex.Normal;
                                                AsFbxMorphSetBlendShapeVertexNormal(pMorphContext, vertex.Index, norm.X, norm.Y, norm.Z);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                finally
                {
                    AsFbxMorphDisposeContext(ref pMorphContext);
                }
            }
        }

    }
}
