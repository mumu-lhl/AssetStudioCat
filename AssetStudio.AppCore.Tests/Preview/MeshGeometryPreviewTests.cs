namespace AssetStudio.AppCore.Tests.Preview;

using System.IO;
using System.Numerics;
using AssetStudio.AppCore.Configuration;
using AssetStudio.AppCore.Indexing;
using AssetStudio.AppCore.Loading;
using AssetStudio.AppCore.Preview;
using Xunit;
using Vector3 = System.Numerics.Vector3;

public class MeshGeometryPreviewTests
{
    [Fact]
    public void MeshGeometryData_StoresExpectedProperties()
    {
        var positions = new float[]
        {
            0f, 0f, 0f,
            1f, 0f, 0f,
            0f, 1f, 0f
        };
        var normals = new float[]
        {
            0f, 0f, 1f,
            0f, 0f, 1f,
            0f, 0f, 1f
        };
        var indices = new uint[] { 0, 1, 2 };

        var min = new Vector3(0f, 0f, 0f);
        var max = new Vector3(1f, 1f, 0f);
        var center = (min + max) * 0.5f;
        var extents = max - min;
        var radius = (max - center).Length();

        var geometry = new MeshGeometryData
        {
            Positions = positions,
            Normals = normals,
            CalculatedNormals = null,
            Colors = null,
            Indices = indices,
            VertexCount = 3,
            TriangleCount = 1,
            Min = min,
            Max = max,
            Center = center,
            Extents = extents,
            BoundingRadius = radius
        };

        Assert.Equal(3, geometry.VertexCount);
        Assert.Equal(1, geometry.TriangleCount);
        Assert.Equal(new Vector3(0.5f, 0.5f, 0f), geometry.Center);
        Assert.True(geometry.BoundingRadius > 0);
        Assert.Equal(3, geometry.Indices.Length);
    }

    [Fact]
    public async Task BangDreamMesh_CanExtractMeshGeometry()
    {
        var bangPath = "/home/mumulhl/data/bangdream-data/data";
        var bundlePath = "/home/mumulhl/data/bangdream-data/data/21fa3452163d363a5ee3fa12074f447998d5dcf2b19f908c1dad863c233f923b";
        if (!File.Exists(bundlePath))
        {
            return;
        }

        var settings = new AppSettings { CustomUnityVersion = "2022.3.21f1" }
            .Normalize(AppDirectories.Detect());
        var layout = new AssetStudio.AppCore.Caching.CacheLayout(settings);
        var loader = new AssetObjectLoader(settings, layout);
        var previewService = new AssetPreviewService(loader, new AssetStudio.AppCore.Caching.MemoryPreviewCache(5), settings);

        var entry = new AssetIndexEntry(
            62199,
            bangPath,
            bundlePath,
            "/home/mumulhl/data/bangdream-data/data/CAB-26925e8705373f3b1e5e7bedf3f61389",
            5798084081777520597,
            43,
            "Mesh",
            "Mesh #5798084081777520597",
            "assets/star/forassetbundle/asneeded/star3d/props/002_01/models/pr_002_01.fbx",
            476200,
            4700);

        var preview = await previewService.LoadAsync(entry, CancellationToken.None);

        Assert.NotNull(preview);
        Assert.NotNull(preview.MeshGeometry);
        Assert.Equal(816, preview.MeshGeometry.VertexCount);
        Assert.Equal(816 * 3, preview.MeshGeometry.Positions.Length);
        Assert.Equal(816 * 3, preview.MeshGeometry.Normals.Length);
        Assert.True(preview.MeshGeometry.Indices.Length > 0);
        Assert.True(preview.MeshGeometry.BoundingRadius > 0);
    }
}
