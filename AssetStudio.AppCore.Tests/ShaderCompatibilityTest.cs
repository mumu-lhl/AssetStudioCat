using AssetStudio;
using AssetStudio.AppCore.Configuration;
using AssetStudio.AppCore.Indexing;
using AssetStudio.AppCore.Loading;
using Xunit;

namespace AssetStudio.AppCore.Tests;

public class ShaderCompatibilityTest
{
    [Fact]
    public void TestBangDreamShaderMaterializeAndConvert()
    {
        var bundlePath = "/home/mumulhl/data/bangdream-data/data/356fb11aad8f2393ab1708cc550aad9eec8a94ad445d9d032c018e9ccf03bd3f";
        if (!File.Exists(bundlePath)) return;

        var manager = new AssetsManager();
        manager.Options.CustomUnityVersion = new UnityVersion("2022.3.21f1");
        manager.LoadFilesAndFolders(bundlePath);

        Assert.NotEmpty(manager.AssetsFileList);
        var file = manager.AssetsFileList[0];
        Shader? shader = null;
        Material? material = null;
        foreach (var obj in file.m_Objects)
        {
            var cid = (ClassIDType)obj.classID;
            if (cid == ClassIDType.Shader)
            {
                var matObj = manager.MaterializeObject(file, obj);
                shader = matObj as Shader;
            }
            else if (cid == ClassIDType.Material)
            {
                var matObj = manager.MaterializeObject(file, obj);
                material = matObj as Material;
            }
        }
        Assert.NotNull(shader);
        Assert.NotNull(shader.m_ParsedForm);
        Assert.NotNull(material);

        var converted = shader.Convert();
        Assert.NotNull(converted);
        Assert.Contains("Shader \"Particles/Standard Unlit\"", converted);
        Assert.Contains("Properties", converted);
        Assert.Contains("SubShader", converted);
        Assert.Contains("Program \"vp\"", converted);
        Assert.Contains("#version 300 es", converted);
    }

    [Fact]
    public void TestPreviewServiceSupportsMaterialAndShader()
    {
        var settings = new AppSettings().Normalize(AppDirectories.Detect());
        var layout = new AssetStudio.AppCore.Caching.CacheLayout(settings);
        var loader = new AssetObjectLoader(settings, layout);
        var previewService = new AssetStudio.AppCore.Preview.AssetPreviewService(
            loader,
            new AssetStudio.AppCore.Caching.MemoryPreviewCache(10),
            settings);

        Assert.True(previewService.Supports("Shader"));
        Assert.True(previewService.Supports("Material"));
    }
}
