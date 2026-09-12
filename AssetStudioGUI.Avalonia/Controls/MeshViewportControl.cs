using System;
using System.Numerics;
using AssetStudio.AppCore.Preview;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Silk.NET.OpenGL;
using Vector3 = System.Numerics.Vector3;

namespace AssetStudioGUI.Avalonia.Controls;

public class MeshViewportControl : OpenGlControlBase
{
    public static readonly StyledProperty<MeshGeometryData?> MeshGeometryProperty =
        AvaloniaProperty.Register<MeshViewportControl, MeshGeometryData?>(nameof(MeshGeometry));

    public static readonly StyledProperty<int> WireframeModeProperty =
        AvaloniaProperty.Register<MeshViewportControl, int>(nameof(WireframeMode), 0);

    public static readonly StyledProperty<int> ShadeModeProperty =
        AvaloniaProperty.Register<MeshViewportControl, int>(nameof(ShadeMode), 0);

    public static readonly StyledProperty<bool> UseCalculatedNormalsProperty =
        AvaloniaProperty.Register<MeshViewportControl, bool>(nameof(UseCalculatedNormals), false);

    private GL? _gl;
    private bool _isEs;

    // GL Objects
    private uint _vao;
    private uint _vboPositions;
    private uint _vboNormals;
    private uint _vboColors;
    private uint _eboIndices;
    private uint _eboLines;
    private int _indexCount;
    private int _lineIndexCount;

    // Shaders
    private uint _shadedProgram;
    private uint _colorProgram;
    private uint _wireframeProgram;

    private bool _needsBufferUpdate;
    private bool _needsNormalBufferUpdate;

    // Camera state
    private float _yaw = -0.785f; // -45 deg
    private float _pitch = 0.35f;  // 20 deg
    private float _distance = 2.8f;
    private Vector3 _target = Vector3.Zero;

    // Mouse interaction state
    private Point _lastPoint;
    private bool _isLeftDown;
    private bool _isRightDown;

    public MeshGeometryData? MeshGeometry
    {
        get => GetValue(MeshGeometryProperty);
        set => SetValue(MeshGeometryProperty, value);
    }

    public int WireframeMode
    {
        get => GetValue(WireframeModeProperty);
        set => SetValue(WireframeModeProperty, value);
    }

    public int ShadeMode
    {
        get => GetValue(ShadeModeProperty);
        set => SetValue(ShadeModeProperty, value);
    }

    public bool UseCalculatedNormals
    {
        get => GetValue(UseCalculatedNormalsProperty);
        set => SetValue(UseCalculatedNormalsProperty, value);
    }

    static MeshViewportControl()
    {
        MeshGeometryProperty.Changed.AddClassHandler<MeshViewportControl>((c, _) => c.OnMeshGeometryChanged());
        WireframeModeProperty.Changed.AddClassHandler<MeshViewportControl>((c, _) => c.RequestNextFrameRendering());
        ShadeModeProperty.Changed.AddClassHandler<MeshViewportControl>((c, _) => c.RequestNextFrameRendering());
        UseCalculatedNormalsProperty.Changed.AddClassHandler<MeshViewportControl>((c, _) => c.OnNormalModeChanged());
    }

    public MeshViewportControl()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public void ResetCamera()
    {
        _yaw = -0.785f;
        _pitch = 0.35f;
        _distance = 2.8f;
        _target = Vector3.Zero;
        RequestNextFrameRendering();
    }

    private void OnMeshGeometryChanged()
    {
        _needsBufferUpdate = true;
        ResetCamera();
    }

    private void OnNormalModeChanged()
    {
        _needsNormalBufferUpdate = true;
        RequestNextFrameRendering();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed)
        {
            _isLeftDown = true;
            _lastPoint = point.Position;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
        else if (point.Properties.IsRightButtonPressed || point.Properties.IsMiddleButtonPressed)
        {
            _isRightDown = true;
            _lastPoint = point.Position;
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        if (point.Properties.IsLeftButtonPressed && e.ClickCount == 2)
        {
            ResetCamera();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isLeftDown && !_isRightDown) return;

        var current = e.GetCurrentPoint(this).Position;
        var delta = current - _lastPoint;
        _lastPoint = current;

        if (_isLeftDown)
        {
            _yaw += (float)delta.X * 0.01f;
            _pitch += (float)delta.Y * 0.01f;
            _pitch = Math.Clamp(_pitch, -1.55f, 1.55f);
            RequestNextFrameRendering();
            e.Handled = true;
        }
        else if (_isRightDown)
        {
            PanCamera((float)delta.X, (float)delta.Y);
            RequestNextFrameRendering();
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) _isLeftDown = false;
        if (!point.Properties.IsRightButtonPressed && !point.Properties.IsMiddleButtonPressed) _isRightDown = false;
        if (!_isLeftDown && !_isRightDown)
        {
            e.Pointer.Capture(null);
        }
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var delta = (float)e.Delta.Y;
        _distance *= MathF.Pow(0.9f, delta);
        _distance = Math.Clamp(_distance, 0.05f, 100.0f);
        RequestNextFrameRendering();
        e.Handled = true;
    }

    private void PanCamera(float deltaX, float deltaY)
    {
        var forward = Vector3.Normalize(new Vector3(
            -MathF.Cos(_pitch) * MathF.Sin(_yaw),
            -MathF.Sin(_pitch),
            -MathF.Cos(_pitch) * MathF.Cos(_yaw)));

        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.Cross(right, forward);

        float panSpeed = 0.0025f * _distance;
        _target += (-right * deltaX + up * deltaY) * panSpeed;
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        base.OnOpenGlInit(gl);
        _gl = GL.GetApi(gl.GetProcAddress);

        var version = _gl.GetStringS(StringName.Version) ?? string.Empty;
        _isEs = version.Contains("OpenGL ES", StringComparison.OrdinalIgnoreCase);

        InitShaders();
        _needsBufferUpdate = true;
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        if (_gl != null)
        {
            if (_vao != 0) { _gl.DeleteVertexArray(_vao); _vao = 0; }
            if (_vboPositions != 0) { _gl.DeleteBuffer(_vboPositions); _vboPositions = 0; }
            if (_vboNormals != 0) { _gl.DeleteBuffer(_vboNormals); _vboNormals = 0; }
            if (_vboColors != 0) { _gl.DeleteBuffer(_vboColors); _vboColors = 0; }
            if (_eboIndices != 0) { _gl.DeleteBuffer(_eboIndices); _eboIndices = 0; }
            if (_eboLines != 0) { _gl.DeleteBuffer(_eboLines); _eboLines = 0; }

            if (_shadedProgram != 0) { _gl.DeleteProgram(_shadedProgram); _shadedProgram = 0; }
            if (_colorProgram != 0) { _gl.DeleteProgram(_colorProgram); _colorProgram = 0; }
            if (_wireframeProgram != 0) { _gl.DeleteProgram(_wireframeProgram); _wireframeProgram = 0; }

            _gl.Dispose();
            _gl = null;
        }
        base.OnOpenGlDeinit(gl);
    }

    private void InitShaders()
    {
        if (_gl == null) return;

        string header = _isEs
            ? "#version 300 es\nprecision highp float;\n"
            : "#version 330 core\n";

        string vsSource = header + @"
layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec3 normalDirection;
layout(location = 2) in vec4 vertexColor;

uniform mat4 modelMatrix;
uniform mat4 viewMatrix;
uniform mat4 projMatrix;

out vec3 vNormal;
out vec4 vColor;

void main()
{
    gl_Position = projMatrix * viewMatrix * modelMatrix * vec4(vertexPosition, 1.0);
    vNormal = mat3(modelMatrix) * normalDirection;
    vColor = vertexColor;
}
";

        string fsShadedSource = header + @"
in vec3 vNormal;
in vec4 vColor;
out vec4 fragColor;

void main()
{
    vec3 n = normalize(vNormal);
    vec3 lightDir1 = normalize(vec3(0.5, 0.8, 0.6));
    float diff1 = max(dot(n, lightDir1), 0.0);

    vec3 lightDir2 = normalize(vec3(-0.5, -0.3, -0.6));
    float diff2 = max(dot(-n, lightDir2), 0.0) * 0.35;

    float ambient = 0.25;
    float lighting = ambient + diff1 * 0.65 + diff2;
    vec3 baseColor = vec3(0.82, 0.85, 0.90);
    fragColor = vec4(baseColor * lighting, 1.0);
}
";

        string fsColorSource = header + @"
in vec3 vNormal;
in vec4 vColor;
out vec4 fragColor;

void main()
{
    vec3 n = normalize(vNormal);
    vec3 lightDir = normalize(vec3(0.5, 0.8, 0.6));
    float diff = max(dot(n, lightDir), 0.0) * 0.6 + 0.4;
    fragColor = vec4(vColor.rgb * diff, vColor.a);
}
";

        string fsWireframeSource = header + @"
out vec4 fragColor;

void main()
{
    fragColor = vec4(0.12, 0.12, 0.12, 1.0);
}
";

        _shadedProgram = CreateProgram(vsSource, fsShadedSource);
        _colorProgram = CreateProgram(vsSource, fsColorSource);
        _wireframeProgram = CreateProgram(vsSource, fsWireframeSource);
    }

    private uint CompileShader(ShaderType type, string source)
    {
        var shader = _gl!.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);

        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status == 0)
        {
            var info = _gl.GetShaderInfoLog(shader);
            _gl.DeleteShader(shader);
            throw new InvalidOperationException($"Shader compilation failed ({type}): {info}");
        }
        return shader;
    }

    private uint CreateProgram(string vertexSource, string fragmentSource)
    {
        var vs = CompileShader(ShaderType.VertexShader, vertexSource);
        var fs = CompileShader(ShaderType.FragmentShader, fragmentSource);

        var program = _gl!.CreateProgram();
        _gl.AttachShader(program, vs);
        _gl.AttachShader(program, fs);

        _gl.BindAttribLocation(program, 0, "vertexPosition");
        _gl.BindAttribLocation(program, 1, "normalDirection");
        _gl.BindAttribLocation(program, 2, "vertexColor");

        _gl.LinkProgram(program);

        _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int status);
        if (status == 0)
        {
            var info = _gl.GetProgramInfoLog(program);
            _gl.DeleteProgram(program);
            _gl.DeleteShader(vs);
            _gl.DeleteShader(fs);
            throw new InvalidOperationException($"Program link failed: {info}");
        }

        _gl.DeleteShader(vs);
        _gl.DeleteShader(fs);
        return program;
    }

    private unsafe void UpdateBuffers(MeshGeometryData mesh)
    {
        if (_gl == null) return;

        if (_vao == 0)
        {
            _vao = _gl.GenVertexArray();
        }
        _gl.BindVertexArray(_vao);

        // Positions
        if (_vboPositions == 0) _vboPositions = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboPositions);
        fixed (float* ptr = mesh.Positions)
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(mesh.Positions.Length * sizeof(float)), ptr, BufferUsageARB.StaticDraw);
        }
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 0, (void*)0);
        _gl.EnableVertexAttribArray(0);

        // Normals
        UploadNormals(mesh);

        // Colors
        if (_vboColors == 0) _vboColors = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboColors);
        if (mesh.Colors != null && mesh.Colors.Length == mesh.VertexCount * 4)
        {
            fixed (float* ptr = mesh.Colors)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(mesh.Colors.Length * sizeof(float)), ptr, BufferUsageARB.StaticDraw);
            }
        }
        else
        {
            var defaultColors = new float[mesh.VertexCount * 4];
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                defaultColors[i * 4] = 0.8f;
                defaultColors[i * 4 + 1] = 0.8f;
                defaultColors[i * 4 + 2] = 0.8f;
                defaultColors[i * 4 + 3] = 1.0f;
            }
            fixed (float* ptr = defaultColors)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(defaultColors.Length * sizeof(float)), ptr, BufferUsageARB.StaticDraw);
            }
        }
        _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, 0, (void*)0);
        _gl.EnableVertexAttribArray(2);

        // Triangles EBO
        if (_eboIndices == 0) _eboIndices = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _eboIndices);
        fixed (uint* ptr = mesh.Indices)
        {
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(mesh.Indices.Length * sizeof(uint)), ptr, BufferUsageARB.StaticDraw);
        }
        _indexCount = mesh.Indices.Length;

        // Line indices EBO (for wireframe)
        var lineIndices = new uint[mesh.TriangleCount * 6];
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            uint i0 = mesh.Indices[t * 3];
            uint i1 = mesh.Indices[t * 3 + 1];
            uint i2 = mesh.Indices[t * 3 + 2];

            lineIndices[t * 6] = i0;
            lineIndices[t * 6 + 1] = i1;
            lineIndices[t * 6 + 2] = i1;
            lineIndices[t * 6 + 3] = i2;
            lineIndices[t * 6 + 4] = i2;
            lineIndices[t * 6 + 5] = i0;
        }

        if (_eboLines == 0) _eboLines = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _eboLines);
        fixed (uint* ptr = lineIndices)
        {
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(lineIndices.Length * sizeof(uint)), ptr, BufferUsageARB.StaticDraw);
        }
        _lineIndexCount = lineIndices.Length;

        // Restore Triangles EBO as default in VAO
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _eboIndices);
        _gl.BindVertexArray(0);
    }

    private unsafe void UploadNormals(MeshGeometryData mesh)
    {
        if (_gl == null || _vao == 0) return;
        _gl.BindVertexArray(_vao);

        var normals = (UseCalculatedNormals && mesh.CalculatedNormals != null)
            ? mesh.CalculatedNormals
            : mesh.Normals;

        if (_vboNormals == 0) _vboNormals = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vboNormals);
        fixed (float* ptr = normals)
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(normals.Length * sizeof(float)), ptr, BufferUsageARB.StaticDraw);
        }
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 0, (void*)0);
        _gl.EnableVertexAttribArray(1);

        _gl.BindVertexArray(0);
    }

    protected override unsafe void OnOpenGlRender(GlInterface glInterface, int fb)
    {
        if (_gl == null) return;

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var width = (int)Math.Max(1, Bounds.Width * scaling);
        var height = (int)Math.Max(1, Bounds.Height * scaling);

        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.ClearColor(0.14f, 0.16f, 0.18f, 1.0f);
        _gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));

        var mesh = MeshGeometry;
        if (mesh == null || mesh.VertexCount == 0 || mesh.TriangleCount == 0)
        {
            return;
        }

        if (_needsBufferUpdate)
        {
            UpdateBuffers(mesh);
            _needsBufferUpdate = false;
            _needsNormalBufferUpdate = false;
        }
        else if (_needsNormalBufferUpdate)
        {
            UploadNormals(mesh);
            _needsNormalBufferUpdate = false;
        }

        if (_vao == 0) return;

        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.Disable(EnableCap.CullFace);

        var center = mesh.Center;
        var extents = mesh.Extents;
        var maxDim = Math.Max(extents.X, Math.Max(extents.Y, extents.Z));
        var modelScale = maxDim > 1e-5f ? 2.0f / maxDim : 1.0f;
        var modelMatrix = Matrix4x4.CreateTranslation(-center) * Matrix4x4.CreateScale(modelScale);

        float camX = _target.X + _distance * MathF.Cos(_pitch) * MathF.Sin(_yaw);
        float camY = _target.Y + _distance * MathF.Sin(_pitch);
        float camZ = _target.Z + _distance * MathF.Cos(_pitch) * MathF.Cos(_yaw);
        var cameraPos = new Vector3(camX, camY, camZ);
        var viewMatrix = CreateLookAtOpenGL(cameraPos, _target, Vector3.UnitY);

        float aspect = (float)width / (float)height;
        var projMatrix = CreatePerspectiveOpenGL(MathF.PI / 4.0f, aspect, 0.05f, 100.0f);

        _gl.BindVertexArray(_vao);

        int wireMode = WireframeMode;
        int shadeMode = ShadeMode;

        // Shaded rendering
        if (wireMode == 0 || wireMode == 2)
        {
            uint program = (shadeMode == 1) ? _colorProgram : _shadedProgram;
            _gl.UseProgram(program);

            SetMatrixUniform(program, "modelMatrix", in modelMatrix);
            SetMatrixUniform(program, "viewMatrix", in viewMatrix);
            SetMatrixUniform(program, "projMatrix", in projMatrix);

            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _eboIndices);
            _gl.DrawElements(PrimitiveType.Triangles, (uint)_indexCount, DrawElementsType.UnsignedInt, (void*)0);
        }

        // Wireframe rendering
        if (wireMode == 1 || wireMode == 2)
        {
            if (wireMode == 2)
            {
                _gl.Enable(EnableCap.PolygonOffsetLine);
                _gl.PolygonOffset(-1.0f, -1.0f);
            }

            _gl.UseProgram(_wireframeProgram);
            SetMatrixUniform(_wireframeProgram, "modelMatrix", in modelMatrix);
            SetMatrixUniform(_wireframeProgram, "viewMatrix", in viewMatrix);
            SetMatrixUniform(_wireframeProgram, "projMatrix", in projMatrix);

            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _eboLines);
            _gl.DrawElements(PrimitiveType.Lines, (uint)_lineIndexCount, DrawElementsType.UnsignedInt, (void*)0);

            if (wireMode == 2)
            {
                _gl.Disable(EnableCap.PolygonOffsetLine);
            }
        }

        _gl.BindVertexArray(0);
    }

    private void SetMatrixUniform(uint program, string name, in Matrix4x4 matrix)
    {
        int loc = _gl!.GetUniformLocation(program, name);
        if (loc >= 0)
        {
            _gl.UniformMatrix4(loc, 1, false, in matrix.M11);
        }
    }

    private static Matrix4x4 CreatePerspectiveOpenGL(float fovRadians, float aspect, float near, float far)
    {
        float tanHalfFov = MathF.Tan(fovRadians * 0.5f);
        return new Matrix4x4
        {
            M11 = 1.0f / (aspect * tanHalfFov),
            M22 = 1.0f / tanHalfFov,
            M33 = -(far + near) / (far - near),
            M34 = -1.0f,
            M43 = -(2.0f * far * near) / (far - near),
            M44 = 0.0f
        };
    }

    private static Matrix4x4 CreateLookAtOpenGL(Vector3 eye, Vector3 target, Vector3 up)
    {
        var zAxis = Vector3.Normalize(eye - target);
        var xAxis = Vector3.Normalize(Vector3.Cross(up, zAxis));
        var yAxis = Vector3.Cross(zAxis, xAxis);

        return new Matrix4x4
        {
            M11 = xAxis.X,
            M12 = yAxis.X,
            M13 = zAxis.X,
            M14 = 0.0f,

            M21 = xAxis.Y,
            M22 = yAxis.Y,
            M23 = zAxis.Y,
            M24 = 0.0f,

            M31 = xAxis.Z,
            M32 = yAxis.Z,
            M33 = zAxis.Z,
            M34 = 0.0f,

            M41 = -Vector3.Dot(xAxis, eye),
            M42 = -Vector3.Dot(yAxis, eye),
            M43 = -Vector3.Dot(zAxis, eye),
            M44 = 1.0f
        };
    }
}
