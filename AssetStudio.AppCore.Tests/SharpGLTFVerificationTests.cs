using System;
using System.IO;
using SysVector2 = System.Numerics.Vector2;
using SysVector3 = System.Numerics.Vector3;
using SysVector4 = System.Numerics.Vector4;
using SysQuaternion = System.Numerics.Quaternion;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using Xunit;

namespace AssetStudio.AppCore.Tests;

public class SharpGLTFVerificationTests
{
    [Fact]
    public void VerifySharpGLTFAnimationAndMeshBuilding()
    {
        var sceneBuilder = new SceneBuilder();
        var rootNode = new NodeBuilder("Root");
        sceneBuilder.AddNode(rootNode);

        var childNode = rootNode.CreateNode("Child");
        childNode.LocalTransform = new SharpGLTF.Transforms.AffineTransform(
            SysVector3.One,
            SysQuaternion.Identity,
            new SysVector3(0, 1, 0));

        // Test animation curves
        var transCurve = childNode.UseTranslation("Default");
        transCurve.WithPoint(0.0f, new SysVector3(0, 1, 0));
        transCurve.WithPoint(1.0f, new SysVector3(0, 2, 0));

        var rotCurve = childNode.UseRotation("Default");
        rotCurve.WithPoint(0.0f, SysQuaternion.Identity);
        rotCurve.WithPoint(1.0f, SysQuaternion.CreateFromAxisAngle(SysVector3.UnitY, 1.0f));

        var scaleCurve = childNode.UseScale("Default");
        scaleCurve.WithPoint(0.0f, SysVector3.One);
        scaleCurve.WithPoint(1.0f, new SysVector3(1, 2, 1));

        // Test mesh building
        var material = new MaterialBuilder("Mat").WithBaseColor(new SysVector4(1, 0, 0, 1));
        var meshBuilder = VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>.CreateCompatibleMesh("TestMesh");
        var prim = meshBuilder.UsePrimitive(material, 3);
        var v0 = new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>(
            new VertexPositionNormal(0, 0, 0, 0, 1, 0),
            new VertexTexture1(new SysVector2(0, 0)));
        var v1 = new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>(
            new VertexPositionNormal(1, 0, 0, 0, 1, 0),
            new VertexTexture1(new SysVector2(1, 0)));
        var v2 = new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>(
            new VertexPositionNormal(0, 1, 0, 0, 1, 0),
            new VertexTexture1(new SysVector2(0, 1)));
        prim.AddTriangle(v0, v1, v2);

        sceneBuilder.AddRigidMesh(meshBuilder, childNode);

        var model = sceneBuilder.ToGltf2();
        Assert.NotNull(model);

        using var ms = new MemoryStream();
        model.WriteGLB(ms);
        Assert.True(ms.Length > 0);
    }
}
