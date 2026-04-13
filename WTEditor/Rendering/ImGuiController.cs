using Hexa.NET.ImGui;
using Silk.NET.OpenGL;
using System.Numerics;
using System.Runtime.InteropServices;

namespace WTEditor.Rendering;

// Based on Silk.NET.OpenGL.Extensions.Hexa.ImGui

public class ImGuiController : IDisposable
{
    private readonly GL _gl;
    private bool _frameBegun;
    private readonly List<char> _pressedChars = new();

    private int _attribLocationTex;
    private int _attribLocationProjMtx;
    private int _attribLocationVtxPos;
    private int _attribLocationVtxUV;
    private int _attribLocationVtxColor;
    private uint _vboHandle;
    private uint _elementsHandle;
    private uint _vertexArrayObject;

    readonly Dictionary<ImTextureID, TextureInfo> _textures = [];
    private Shader _shader;

    private int _windowWidth;
    private int _windowHeight;
    private Vector2 _scaleFactor = Vector2.One;

    public ImGuiContextPtr Context;

    public ImGuiController(GL gl, int width, int height)
    {
        _gl = gl;
        _windowWidth = width;
        _windowHeight = height;

        Context = ImGui.CreateContext();
        ImGui.SetCurrentContext(Context);

        var io = ImGui.GetIO();
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset | ImGuiBackendFlags.RendererHasTextures;
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;

        ImGui.StyleColorsDark();

        CreateDeviceResources();
        SetPerFrameImGuiData(1f / 60f);

        BeginFrame();
    }

    private void BeginFrame()
    {
        ImGui.NewFrame();
        _frameBegun = true;
    }

    private void CreateDeviceResources()
    {
        _gl.GetInteger(GLEnum.TextureBinding2D, out int lastTexture);
        _gl.GetInteger(GLEnum.ArrayBufferBinding, out int lastArrayBuffer);
        _gl.GetInteger(GLEnum.VertexArrayBinding, out int lastVertexArray);

        string vertexSource = @"#version 330
layout (location = 0) in vec2 Position;
layout (location = 1) in vec2 UV;
layout (location = 2) in vec4 Color;
uniform mat4 ProjMtx;
out vec2 Frag_UV;
out vec4 Frag_Color;
void main()
{
    Frag_UV = UV;
    Frag_Color = Color;
    gl_Position = ProjMtx * vec4(Position.xy,0,1);
}";

        string fragmentSource = @"#version 330
in vec2 Frag_UV;
in vec4 Frag_Color;
uniform sampler2D Texture;
layout (location = 0) out vec4 Out_Color;
void main()
{
    Out_Color = Frag_Color * texture(Texture, Frag_UV.st);
}";

        _shader = new Shader(_gl, vertexSource, fragmentSource);

        _attribLocationTex = _shader.GetUniformLocation("Texture");
        _attribLocationProjMtx = _shader.GetUniformLocation("ProjMtx");
        _attribLocationVtxPos = _shader.GetAttribLocation("Position");
        _attribLocationVtxUV = _shader.GetAttribLocation("UV");
        _attribLocationVtxColor = _shader.GetAttribLocation("Color");

        _vboHandle = _gl.GenBuffer();
        _elementsHandle = _gl.GenBuffer();

        _gl.BindTexture(GLEnum.Texture2D, (uint)lastTexture);
        _gl.BindBuffer(GLEnum.ArrayBuffer, (uint)lastArrayBuffer);
        _gl.BindVertexArray((uint)lastVertexArray);

        _gl.CheckGlError("End of ImGui setup");
    }

    public void WindowResized(int width, int height)
    {
        _windowWidth = width;
        _windowHeight = height;
    }

    public void SetPerFrameImGuiData(float deltaSeconds)
    {
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(_windowWidth / _scaleFactor.X, _windowHeight / _scaleFactor.Y);
        io.DisplayFramebufferScale = _scaleFactor;
        io.DeltaTime = deltaSeconds;
    }

    public void Update(float deltaSeconds)
    {
        var oldCtx = ImGui.GetCurrentContext();

        if (oldCtx != Context)
        {
            ImGui.SetCurrentContext(Context);
        }

        if (_frameBegun)
        {
            ImGui.Render();
        }

        SetPerFrameImGuiData(deltaSeconds);
        UpdateImGuiInput();

        _frameBegun = true;
        ImGui.NewFrame();

        if (oldCtx != Context)
        {
            ImGui.SetCurrentContext(oldCtx);
        }
    }

    private void UpdateImGuiInput()
    {
        var io = ImGui.GetIO();

        foreach (var c in _pressedChars)
        {
            io.AddInputCharacter(c);
        }

        _pressedChars.Clear();
    }

    public void Render()
    {
        if (_frameBegun)
        {
            var oldCtx = ImGui.GetCurrentContext();

            if (oldCtx != Context)
            {
                ImGui.SetCurrentContext(Context);
            }

            _frameBegun = false;
            ImGui.Render();
            RenderImDrawData(ImGui.GetDrawData());

            if (oldCtx != Context)
            {
                ImGui.SetCurrentContext(oldCtx);
            }
        }
    }

    private unsafe void SetupRenderState(ImDrawDataPtr drawDataPtr, int framebufferWidth, int framebufferHeight)
    {
        _gl.Enable(GLEnum.Blend);
        _gl.BlendEquation(GLEnum.FuncAdd);
        _gl.BlendFuncSeparate(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha, GLEnum.One, GLEnum.OneMinusSrcAlpha);
        _gl.Disable(GLEnum.CullFace);
        _gl.Disable(GLEnum.DepthTest);
        _gl.Disable(GLEnum.StencilTest);
        _gl.Enable(GLEnum.ScissorTest);
        _gl.Disable(GLEnum.PrimitiveRestart);
        _gl.PolygonMode(GLEnum.FrontAndBack, GLEnum.Fill);

        float L = drawDataPtr.DisplayPos.X;
        float R = drawDataPtr.DisplayPos.X + drawDataPtr.DisplaySize.X;
        float T = drawDataPtr.DisplayPos.Y;
        float B = drawDataPtr.DisplayPos.Y + drawDataPtr.DisplaySize.Y;

        Span<float> orthoProjection = stackalloc float[]
        {
            2.0f / (R - L), 0.0f, 0.0f, 0.0f,
            0.0f, 2.0f / (T - B), 0.0f, 0.0f,
            0.0f, 0.0f, -1.0f, 0.0f,
            (R + L) / (L - R), (T + B) / (B - T), 0.0f, 1.0f,
        };

        _shader.UseShader();
        _gl.Uniform1(_attribLocationTex, 0);
        _gl.UniformMatrix4(_attribLocationProjMtx, 1, false, orthoProjection);
        _gl.CheckGlError("Projection");

        _gl.BindSampler(0, 0);

        _vertexArrayObject = _gl.GenVertexArray();
        _gl.BindVertexArray(_vertexArrayObject);
        _gl.CheckGlError("VAO");

        _gl.BindBuffer(GLEnum.ArrayBuffer, _vboHandle);
        _gl.BindBuffer(GLEnum.ElementArrayBuffer, _elementsHandle);
        _gl.EnableVertexAttribArray((uint)_attribLocationVtxPos);
        _gl.EnableVertexAttribArray((uint)_attribLocationVtxUV);
        _gl.EnableVertexAttribArray((uint)_attribLocationVtxColor);
        _gl.VertexAttribPointer((uint)_attribLocationVtxPos, 2, GLEnum.Float, false, (uint)sizeof(ImDrawVert), (void*)0);
        _gl.VertexAttribPointer((uint)_attribLocationVtxUV, 2, GLEnum.Float, false, (uint)sizeof(ImDrawVert), (void*)8);
        _gl.VertexAttribPointer((uint)_attribLocationVtxColor, 4, GLEnum.UnsignedByte, true, (uint)sizeof(ImDrawVert), (void*)16);
    }

    private unsafe void RenderImDrawData(ImDrawDataPtr drawDataPtr)
    {
        int framebufferWidth = (int)(drawDataPtr.DisplaySize.X * drawDataPtr.FramebufferScale.X);
        int framebufferHeight = (int)(drawDataPtr.DisplaySize.Y * drawDataPtr.FramebufferScale.Y);
        if (framebufferWidth <= 0 || framebufferHeight <= 0)
            return;

        _gl.GetInteger(GLEnum.ActiveTexture, out int lastActiveTexture);
        _gl.ActiveTexture(GLEnum.Texture0);

        _gl.GetInteger(GLEnum.CurrentProgram, out int lastProgram);
        _gl.GetInteger(GLEnum.TextureBinding2D, out int lastTexture);
        _gl.GetInteger(GLEnum.SamplerBinding, out int lastSampler);
        _gl.GetInteger(GLEnum.ArrayBufferBinding, out int lastArrayBuffer);
        _gl.GetInteger(GLEnum.VertexArrayBinding, out int lastVertexArrayObject);

        Span<int> lastPolygonMode = stackalloc int[2];
        _gl.GetInteger(GLEnum.PolygonMode, lastPolygonMode);

        Span<int> lastScissorBox = stackalloc int[4];
        _gl.GetInteger(GLEnum.ScissorBox, lastScissorBox);

        _gl.GetInteger(GLEnum.BlendSrcRgb, out int lastBlendSrcRgb);
        _gl.GetInteger(GLEnum.BlendDstRgb, out int lastBlendDstRgb);
        _gl.GetInteger(GLEnum.BlendSrcAlpha, out int lastBlendSrcAlpha);
        _gl.GetInteger(GLEnum.BlendDstAlpha, out int lastBlendDstAlpha);
        _gl.GetInteger(GLEnum.BlendEquationRgb, out int lastBlendEquationRgb);
        _gl.GetInteger(GLEnum.BlendEquationAlpha, out int lastBlendEquationAlpha);

        bool lastEnableBlend = _gl.IsEnabled(GLEnum.Blend);
        bool lastEnableCullFace = _gl.IsEnabled(GLEnum.CullFace);
        bool lastEnableDepthTest = _gl.IsEnabled(GLEnum.DepthTest);
        bool lastEnableStencilTest = _gl.IsEnabled(GLEnum.StencilTest);
        bool lastEnableScissorTest = _gl.IsEnabled(GLEnum.ScissorTest);
        bool lastEnablePrimitiveRestart = _gl.IsEnabled(GLEnum.PrimitiveRestart);

        _gl.Viewport(0, 0, (uint)framebufferWidth, (uint)framebufferHeight);

        for (int i = 0; i < drawDataPtr.Textures.Size; i++)
        {
            UpdateTexture(drawDataPtr.Textures[i]);
        }

        SetupRenderState(drawDataPtr, framebufferWidth, framebufferHeight);

        Vector2 clipOff = drawDataPtr.DisplayPos;
        Vector2 clipScale = drawDataPtr.FramebufferScale;

        for (int n = 0; n < drawDataPtr.CmdListsCount; n++)
        {
            ImDrawListPtr cmdListPtr = drawDataPtr.CmdLists[n];

            _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(cmdListPtr.VtxBuffer.Size * sizeof(ImDrawVert)), (void*)cmdListPtr.VtxBuffer.Data, GLEnum.StreamDraw);
            _gl.CheckGlError($"Data Vert {n}");
            _gl.BufferData(GLEnum.ElementArrayBuffer, (nuint)(cmdListPtr.IdxBuffer.Size * sizeof(ushort)), (void*)cmdListPtr.IdxBuffer.Data, GLEnum.StreamDraw);
            _gl.CheckGlError($"Data Idx {n}");

            for (int cmd_i = 0; cmd_i < cmdListPtr.CmdBuffer.Size; cmd_i++)
            {
                ImDrawCmd cmdPtr = cmdListPtr.CmdBuffer[cmd_i];

                if (cmdPtr.UserCallback != null)
                {
                    throw new NotImplementedException();
                }
                else
                {
                    Vector4 clipRect;
                    clipRect.X = (cmdPtr.ClipRect.X - clipOff.X) * clipScale.X;
                    clipRect.Y = (cmdPtr.ClipRect.Y - clipOff.Y) * clipScale.Y;
                    clipRect.Z = (cmdPtr.ClipRect.Z - clipOff.X) * clipScale.X;
                    clipRect.W = (cmdPtr.ClipRect.W - clipOff.Y) * clipScale.Y;

                    if (clipRect.X < framebufferWidth && clipRect.Y < framebufferHeight && clipRect.Z >= 0.0f && clipRect.W >= 0.0f)
                    {
                        _gl.Scissor((int)clipRect.X, (int)(framebufferHeight - clipRect.W), (uint)(clipRect.Z - clipRect.X), (uint)(clipRect.W - clipRect.Y));
                        _gl.CheckGlError("Scissor");

                        _gl.BindTexture(GLEnum.Texture2D, (uint)(nuint)cmdPtr.GetTexID());
                        _gl.CheckGlError("Texture");

                        _gl.DrawElementsBaseVertex(GLEnum.Triangles, cmdPtr.ElemCount, GLEnum.UnsignedShort, (void*)(cmdPtr.IdxOffset * sizeof(ushort)), (int)cmdPtr.VtxOffset);
                        _gl.CheckGlError("Draw");
                    }
                }
            }
        }

        _gl.DeleteVertexArray(_vertexArrayObject);
        _vertexArrayObject = 0;

        _gl.UseProgram((uint)lastProgram);
        _gl.BindTexture(GLEnum.Texture2D, (uint)lastTexture);
        _gl.BindSampler(0, (uint)lastSampler);
        _gl.ActiveTexture((GLEnum)lastActiveTexture);
        _gl.BindVertexArray((uint)lastVertexArrayObject);
        _gl.BindBuffer(GLEnum.ArrayBuffer, (uint)lastArrayBuffer);
        _gl.BlendEquationSeparate((GLEnum)lastBlendEquationRgb, (GLEnum)lastBlendEquationAlpha);
        _gl.BlendFuncSeparate((GLEnum)lastBlendSrcRgb, (GLEnum)lastBlendDstRgb, (GLEnum)lastBlendSrcAlpha, (GLEnum)lastBlendDstAlpha);

        if (lastEnableBlend)
            _gl.Enable(GLEnum.Blend);
        else
            _gl.Disable(GLEnum.Blend);

        if (lastEnableCullFace)
            _gl.Enable(GLEnum.CullFace);
        else
            _gl.Disable(GLEnum.CullFace);

        if (lastEnableDepthTest)
            _gl.Enable(GLEnum.DepthTest);
        else
            _gl.Disable(GLEnum.DepthTest);

        if (lastEnableStencilTest)
            _gl.Enable(GLEnum.StencilTest);
        else
            _gl.Disable(GLEnum.StencilTest);

        if (lastEnableScissorTest)
            _gl.Enable(GLEnum.ScissorTest);
        else
            _gl.Disable(GLEnum.ScissorTest);

        if (lastEnablePrimitiveRestart)
            _gl.Enable(GLEnum.PrimitiveRestart);
        else
            _gl.Disable(GLEnum.PrimitiveRestart);

        _gl.PolygonMode(GLEnum.FrontAndBack, (GLEnum)lastPolygonMode[0]);

        _gl.Scissor(lastScissorBox[0], lastScissorBox[1], (uint)lastScissorBox[2], (uint)lastScissorBox[3]);
    }

    public void UpdateTexture(ImTextureDataPtr textureData)
    {
        switch (textureData.Status)
        {
            case ImTextureStatus.WantCreate:
                CreateTexture(textureData);
                break;

            case ImTextureStatus.WantUpdates:
                UpdateTextureData(textureData);
                break;

            case ImTextureStatus.WantDestroy:
                DestroyTexture(textureData);
                break;

            case ImTextureStatus.Ok:
                break;
        }
    }

    public unsafe void CreateTexture(ImTextureDataPtr textureData)
    {
        _gl.GetInteger(GetPName.TextureBinding2D, out int lastTexture);
        bool srgb = textureData.Format == ImTextureFormat.Rgba32;
        Texture texture = new(_gl, textureData.Width, textureData.Height, IntPtr.Zero, false, srgb);

        if (textureData.Pixels != null)
        {
            int pixelCount = textureData.Width * textureData.Height;
            int bytesPerPixel = srgb ? 4 : 1;
            int dataSize = pixelCount * bytesPerPixel;
            byte[] managedData = new byte[dataSize];
            Marshal.Copy(new IntPtr(textureData.Pixels), managedData, 0, dataSize);
            fixed (byte* ptr = managedData)
            {
                texture.SetData(new IntPtr(ptr));
            }
            texture.SetMagFilter(TextureMagFilter.Linear);
            texture.SetMinFilter(TextureMinFilter.Linear);
        }

        ImTextureID texId = new(texture.GlTexture);
        textureData.SetTexID(texId);
        _textures[texId] = new TextureInfo(texture, true);
        _gl.BindTexture(GLEnum.Texture2D, (uint)lastTexture);
        textureData.SetStatus(ImTextureStatus.Ok);
    }

    public unsafe void UpdateTextureData(ImTextureDataPtr textureData)
    {
        ImTextureID oldTexId = textureData.GetTexID();
        if (!_textures.TryGetValue(oldTexId, out TextureInfo textureInfo))
        {
            return;
        }
        Texture texture = textureInfo.Texture;
        _gl.GetInteger(GetPName.TextureBinding2D, out int lastTexture);
        bool srgb = textureData.Format == ImTextureFormat.Rgba32;
        if (texture.Width != textureData.Width
            || texture.Height != textureData.Height
            || texture.InternalFormat != (srgb ? SizedInternalFormat.Srgb8Alpha8 : SizedInternalFormat.Rgba8))
        {
            texture.Dispose();
            texture = new(_gl, textureData.Width, textureData.Height, IntPtr.Zero, false, srgb);
            texture.Bind();
            texture.SetMagFilter(TextureMagFilter.Linear);
            texture.SetMinFilter(TextureMinFilter.Linear);
            textureInfo.Texture = texture;
            _textures.Remove(oldTexId);
            ImTextureID newTexId = new(texture.GlTexture);
            textureData.SetTexID(newTexId);
            _textures[newTexId] = textureInfo;
        }
        if (textureData.Pixels != null)
        {
            int pixelCount = textureData.Width * textureData.Height;
            int bytesPerPixel = srgb ? 4 : 1;
            int dataSize = pixelCount * bytesPerPixel;
            byte[] managedData = new byte[dataSize];
            Marshal.Copy(new IntPtr(textureData.Pixels), managedData, 0, dataSize);
            fixed (byte* ptr = managedData)
            {
                texture.SetData(new IntPtr(ptr));
            }
        }
        _gl.BindTexture(GLEnum.Texture2D, (uint)lastTexture);
        textureData.SetStatus(ImTextureStatus.Ok);
    }

    public void DestroyTexture(ImTextureDataPtr textureData)
    {
        ImTextureID texId = textureData.GetTexID();
        if (_textures.TryGetValue(texId, out TextureInfo textureInfo))
        {
            if (textureInfo.IsManaged)
            {
                textureInfo.Texture?.Dispose();
            }
            _textures.Remove(texId);
        }
    }

    public void SetMousePosition(float x, float y)
    {
        var io = ImGui.GetIO();
        io.MousePos = new Vector2(x, y);
    }

    public void SetMouseButton(int button, bool pressed)
    {
        var io = ImGui.GetIO();
        if (button >= 0 && button < 5)
        {
            io.MouseDown[button] = pressed;
        }
    }

    public void AddMouseWheel(float delta)
    {
        var io = ImGui.GetIO();
        io.MouseWheel += delta;
    }

    public void SetKeyDown(ImGuiKey key, bool pressed)
    {
        var io = ImGui.GetIO();
        io.AddKeyEvent(key, pressed);
    }

    public void AddInputCharacter(char c)
    {
        _pressedChars.Add(c);
    }

    public void SetControlKey(bool pressed) => SetKeyDown(ImGuiKey.LeftCtrl, pressed);
    public void SetShiftKey(bool pressed) => SetKeyDown(ImGuiKey.LeftShift, pressed);
    public void SetAltKey(bool pressed) => SetKeyDown(ImGuiKey.LeftAlt, pressed);
    public void SetSuperKey(bool pressed) => SetKeyDown(ImGuiKey.LeftSuper, pressed);

    public static ImGuiKey ConvertWpfKeyToImGui(System.Windows.Input.Key key)
    {
        return key switch
        {
            System.Windows.Input.Key.Tab => ImGuiKey.Tab,
            System.Windows.Input.Key.Left => ImGuiKey.LeftArrow,
            System.Windows.Input.Key.Right => ImGuiKey.RightArrow,
            System.Windows.Input.Key.Up => ImGuiKey.UpArrow,
            System.Windows.Input.Key.Down => ImGuiKey.DownArrow,
            System.Windows.Input.Key.PageUp => ImGuiKey.PageUp,
            System.Windows.Input.Key.PageDown => ImGuiKey.PageDown,
            System.Windows.Input.Key.Home => ImGuiKey.Home,
            System.Windows.Input.Key.End => ImGuiKey.End,
            System.Windows.Input.Key.Insert => ImGuiKey.Insert,
            System.Windows.Input.Key.Delete => ImGuiKey.Delete,
            System.Windows.Input.Key.Back => ImGuiKey.Backspace,
            System.Windows.Input.Key.Space => ImGuiKey.Space,
            System.Windows.Input.Key.Enter => ImGuiKey.Enter,
            System.Windows.Input.Key.Escape => ImGuiKey.Escape,
            System.Windows.Input.Key.A => ImGuiKey.A,
            System.Windows.Input.Key.C => ImGuiKey.C,
            System.Windows.Input.Key.V => ImGuiKey.V,
            System.Windows.Input.Key.X => ImGuiKey.X,
            System.Windows.Input.Key.Y => ImGuiKey.Y,
            System.Windows.Input.Key.Z => ImGuiKey.Z,
            _ => ImGuiKey.None
        };
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_vboHandle);
        _gl.DeleteBuffer(_elementsHandle);
        _gl.DeleteVertexArray(_vertexArrayObject);

        var platformIo = ImGui.GetPlatformIO();
        for (int i = 0; i < platformIo.Textures.Size; i++)
        {
            uint tex = (uint)platformIo.Textures[i].TexID.Handle;
            if (tex != 0)
            {
                _gl.DeleteTexture(tex);
            }
        }
        _shader.Dispose();

        ImGui.DestroyContext(Context);

        GC.SuppressFinalize(this);
    }
}
