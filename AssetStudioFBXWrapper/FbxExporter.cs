using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace AssetStudio.FbxInterop
{
    internal sealed class FbxExporter : IDisposable
    {

        private FbxExporterContext _context;

        private readonly string _fileName;
        private readonly IImported _imported;
        private readonly Fbx.Settings _settings;

        internal FbxExporter(string fileName, IImported imported, Fbx.Settings fbxSettings)
        {
            _context = new FbxExporterContext();

            _fileName = fileName;
            _imported = imported;
            _settings = fbxSettings;
        }

        ~FbxExporter()
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
            if (disposing)
            {
                _context.Dispose();
            }

            IsDisposed = true;
        }

        private void Initialize()
        {
            var is60Fps = _imported.AnimationList.Count > 0 && _imported.AnimationList[0].SampleRate.Equals(60.0f);

            _context.Initialize(_fileName, _settings, is60Fps);

            if (!_settings.ExportAllNodes)
            {
                var framePaths = SearchHierarchy();

                _context.SetFramePaths(framePaths);
            }
        }

        internal void ExportAll()
        {
            Initialize();

            var meshFrames = new List<ImportedFrame>();

            ExportRootFrame(meshFrames);

            if (_imported.MeshList != null)
            {
                SetJointsFromImportedMeshes();

                PrepareMaterials();

                ExportMeshFrames(meshFrames);
            }
            else
            {
                SetJointsNode(_imported.RootFrame, null, true);
            }

            if (_settings.ExportBlendShape)
            {
                ExportMorphs();
            }

            if (_settings.ExportAnimations)
            {
                ExportAnimations(_settings.EulerFilter, _settings.FilterPrecision);
            }

            ExportScene();
        }

        private void ExportMorphs()
        {
            _context.ExportMorphs(_imported.RootFrame, _imported.MorphList);
        }

        private void ExportAnimations(bool eulerFilter, float filterPrecision)
        {
            _context.ExportAnimations(_imported.RootFrame, _imported.AnimationList, eulerFilter, filterPrecision);
        }

        private void ExportRootFrame(List<ImportedFrame> meshFrames)
        {
            _context.ExportFrame(_imported.MeshList, meshFrames, _imported.RootFrame);
        }

        private void ExportScene()
        {
            _context.ExportScene();
        }

        private void SetJointsFromImportedMeshes()
        {
            if (!_settings.ExportSkins)
            {
                return;
            }

            Debug.Assert(_imported.MeshList != null);

            var bonePaths = new HashSet<string>();

            foreach (var mesh in _imported.MeshList)
            {
                var boneList = mesh.BoneList;

                if (boneList != null)
                {
                    foreach (var bone in boneList)
                    {
                        bonePaths.Add(bone.Path);
                    }
                }
            }

            SetJointsNode(_imported.RootFrame, bonePaths, _settings.CastToBone);
        }

        private void SetJointsNode(ImportedFrame rootFrame, HashSet<string> bonePaths, bool castToBone)
        {
            _context.SetJointsNode(rootFrame, bonePaths, castToBone, _settings.BoneSize);
        }

        private void PrepareMaterials()
        {
            _context.PrepareMaterials(_imported.MaterialList.Count, _imported.TextureList.Count);
        }

        private void ExportMeshFrames(List<ImportedFrame> meshFrames)
        {
            var meshList = _imported.MeshList;
            var meshMap = new Dictionary<string, ImportedMesh>(meshList?.Count ?? 0);
            if (meshList != null)
            {
                for (var i = 0; i < meshList.Count; i++)
                {
                    var mesh = meshList[i];
                    if (mesh.Path != null && !meshMap.ContainsKey(mesh.Path))
                    {
                        meshMap[mesh.Path] = mesh;
                    }
                }
            }

            var materialList = _imported.MaterialList;
            var materialMap = new Dictionary<string, ImportedMaterial>(materialList?.Count ?? 0);
            if (materialList != null)
            {
                for (var i = 0; i < materialList.Count; i++)
                {
                    var mat = materialList[i];
                    if (mat.Name != null && !materialMap.ContainsKey(mat.Name))
                    {
                        materialMap[mat.Name] = mat;
                    }
                }
            }

            var textureList = _imported.TextureList;
            var textureMap = new Dictionary<string, ImportedTexture>(textureList?.Count ?? 0);
            if (textureList != null)
            {
                for (var i = 0; i < textureList.Count; i++)
                {
                    var tex = textureList[i];
                    if (tex.Name != null && !textureMap.ContainsKey(tex.Name))
                    {
                        textureMap[tex.Name] = tex;
                    }
                }
            }

            for (var i = 0; i < meshFrames.Count; i++)
            {
                var meshFrame = meshFrames[i];
                if (meshMap.TryGetValue(meshFrame.Path, out var mesh))
                {
                    _context.ExportMeshFromFrame(meshFrame, mesh, materialMap, textureMap, _settings);
                }
            }
        }

        private HashSet<string> SearchHierarchy()
        {
            if (_imported.MeshList == null || _imported.MeshList.Count == 0)
            {
                return null;
            }

            var exportFrames = new HashSet<string>();

            SearchHierarchy(_imported.RootFrame, _imported.MeshList, exportFrames);

            return exportFrames;
        }

        private static void SearchHierarchy(ImportedFrame rootFrame, List<ImportedMesh> meshList, HashSet<string> exportFrames)
        {
            var frameByPath = new Dictionary<string, ImportedFrame>();
            var frameStack = new Stack<ImportedFrame>();

            frameStack.Push(rootFrame);

            while (frameStack.Count > 0)
            {
                var frame = frameStack.Pop();
                frameByPath[frame.Path] = frame;

                for (var i = frame.Count - 1; i >= 0; i -= 1)
                {
                    frameStack.Push(frame[i]);
                }
            }

            for (var m = 0; m < meshList.Count; m++)
            {
                var mesh = meshList[m];
                if (mesh.Path == null || !frameByPath.TryGetValue(mesh.Path, out var meshFrame))
                {
                    continue;
                }

                var parent = meshFrame;
                while (parent != null && exportFrames.Add(parent.Path))
                {
                    parent = parent.Parent;
                }

                var boneList = mesh.BoneList;
                if (boneList != null)
                {
                    for (var b = 0; b < boneList.Count; b++)
                    {
                        var bone = boneList[b];
                        if (bone.Path != null && !exportFrames.Contains(bone.Path) && frameByPath.TryGetValue(bone.Path, out var boneFrame))
                        {
                            var boneParent = boneFrame;
                            while (boneParent != null && exportFrames.Add(boneParent.Path))
                            {
                                boneParent = boneParent.Parent;
                            }
                        }
                    }
                }
            }
        }

    }
}
