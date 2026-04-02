using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ImGuiNET;
using Optinstaller.Platform;
using Vortice;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;

namespace Optinstaller.UI;

internal sealed unsafe class Dx11ImGuiRenderer : IDisposable
{
    private const string VertexShaderSource = @"
cbuffer vertexBuffer : register(b0)
{
    float4x4 ProjectionMatrix;
};

struct VS_INPUT
{
    float2 pos : POSITION;
    float4 col : COLOR0;
    float2 uv  : TEXCOORD0;
};

struct PS_INPUT
{
    float4 pos : SV_POSITION;
    float4 col : COLOR0;
    float2 uv  : TEXCOORD0;
};

PS_INPUT main(VS_INPUT input)
{
    PS_INPUT output;
    output.pos = mul(ProjectionMatrix, float4(input.pos.xy, 0.0f, 1.0f));
    output.col = input.col;
    output.uv = input.uv;
    return output;
}";

    private const string PixelShaderSource = @"
SamplerState sampler0 : register(s0);
Texture2D texture0 : register(t0);

struct PS_INPUT
{
    float4 pos : SV_POSITION;
    float4 col : COLOR0;
    float2 uv  : TEXCOORD0;
};

float4 main(PS_INPUT input) : SV_Target
{
    return input.col * texture0.Sample(sampler0, input.uv);
}";

    private readonly nint _hwnd;
    private readonly Dictionary<nint, ID3D11ShaderResourceView> _textures = new();

    private ID3D11Device? _device;
    private ID3D11DeviceContext? _deviceContext;
    private IDXGISwapChain? _swapChain;
    private ID3D11RenderTargetView? _renderTargetView;

    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _pixelShader;
    private ID3D11InputLayout? _inputLayout;
    private ID3D11Buffer? _vertexConstantBuffer;
    private ID3D11SamplerState? _fontSampler;
    private ID3D11ShaderResourceView? _fontShaderResourceView;
    private ID3D11BlendState? _blendState;
    private ID3D11RasterizerState? _rasterizerState;
    private ID3D11DepthStencilState? _depthStencilState;
    private ID3D11Buffer? _vertexBuffer;
    private ID3D11Buffer? _indexBuffer;

    private int _vertexBufferSize = 5000;
    private int _indexBufferSize = 10000;
    private int _width;
    private int _height;
    private nint _fontTextureId;
    private nint _nextTextureId = 1;
    private bool _disposed;

    public Dx11ImGuiRenderer(nint hwnd, int width, int height, Action<ImGuiIOPtr> configureIo, Action<ImGuiIOPtr> loadFonts, Action applyTheme)
    {
        _hwnd = hwnd;
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);

        CreateDeviceResources();

        ImGui.CreateContext();

        var io = ImGui.GetIO();
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;

        configureIo(io);
        loadFonts(io);
        applyTheme();

        CreateDeviceObjects();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        ImGui.DestroyContext();

        var disposedTextures = new HashSet<ID3D11ShaderResourceView>();
        foreach (var texture in _textures.Values)
        {
            if (disposedTextures.Add(texture))
            {
                texture.Dispose();
            }
        }

        if (_fontShaderResourceView != null && disposedTextures.Add(_fontShaderResourceView))
        {
            _fontShaderResourceView.Dispose();
        }

        _textures.Clear();
        _fontShaderResourceView = null;
        _fontTextureId = 0;
        _nextTextureId = 1;

        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();
        _vertexConstantBuffer?.Dispose();
        _inputLayout?.Dispose();
        _vertexShader?.Dispose();
        _pixelShader?.Dispose();
        _fontSampler?.Dispose();
        _blendState?.Dispose();
        _rasterizerState?.Dispose();
        _depthStencilState?.Dispose();
        _renderTargetView?.Dispose();
        _swapChain?.Dispose();

        _deviceContext?.ClearState();
        _deviceContext?.Dispose();
        _device?.Dispose();
    }

    public void BeginFrame(float deltaTime, int width, int height)
    {
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(Math.Max(1, width), Math.Max(1, height));
        io.DisplayFramebufferScale = Vector2.One;
        io.DeltaTime = deltaTime > 0f ? deltaTime : 1f / 60f;

        ImGui.NewFrame();
    }

    public void Render(Vector4 clearColor)
    {
        if (_deviceContext == null || _renderTargetView == null || _swapChain == null)
        {
            return;
        }

        ImGui.Render();

        _deviceContext.OMSetRenderTargets(new[] { _renderTargetView }, null);
        _deviceContext.ClearRenderTargetView(_renderTargetView, new Color4(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W));

        RenderDrawData(ImGui.GetDrawData());
        _swapChain.Present(1, PresentFlags.None);
    }

    public void Resize(int width, int height)
    {
        if (_swapChain == null || _deviceContext == null || width <= 0 || height <= 0)
        {
            return;
        }

        _width = width;
        _height = height;

        _renderTargetView?.Dispose();
        _renderTargetView = null;

        _deviceContext.OMSetRenderTargets(Array.Empty<ID3D11RenderTargetView>(), null);
        _swapChain.ResizeBuffers(0, (uint)_width, (uint)_height, Format.Unknown, SwapChainFlags.None);
        CreateRenderTarget();
    }

    public bool HandleMessage(uint msg, nuint wParam, nint lParam)
    {
        var io = ImGui.GetIO();

        switch (msg)
        {
            case Win32Native.WM_LBUTTONDOWN:
                Win32Native.SetCapture(_hwnd);
                io.AddMouseButtonEvent(0, true);
                return true;

            case Win32Native.WM_LBUTTONUP:
                Win32Native.ReleaseCapture();
                io.AddMouseButtonEvent(0, false);
                return true;

            case Win32Native.WM_RBUTTONDOWN:
                Win32Native.SetCapture(_hwnd);
                io.AddMouseButtonEvent(1, true);
                return true;

            case Win32Native.WM_RBUTTONUP:
                Win32Native.ReleaseCapture();
                io.AddMouseButtonEvent(1, false);
                return true;

            case Win32Native.WM_MBUTTONDOWN:
                Win32Native.SetCapture(_hwnd);
                io.AddMouseButtonEvent(2, true);
                return true;

            case Win32Native.WM_MBUTTONUP:
                Win32Native.ReleaseCapture();
                io.AddMouseButtonEvent(2, false);
                return true;

            case Win32Native.WM_MOUSEMOVE:
                io.AddMousePosEvent(Win32Native.GetXFromLParam(lParam), Win32Native.GetYFromLParam(lParam));
                return true;

            case Win32Native.WM_MOUSEWHEEL:
                io.AddMouseWheelEvent(0f, Win32Native.GetWheelDeltaWParam(wParam) / 120.0f);
                return true;

            case Win32Native.WM_MOUSEHWHEEL:
                io.AddMouseWheelEvent(Win32Native.GetWheelDeltaWParam(wParam) / 120.0f, 0f);
                return true;

            case Win32Native.WM_KEYDOWN:
            case Win32Native.WM_SYSKEYDOWN:
                UpdateModifiers(io);
                AddKeyEvent(io, (int)wParam, true);
                return true;

            case Win32Native.WM_KEYUP:
            case Win32Native.WM_SYSKEYUP:
                UpdateModifiers(io);
                AddKeyEvent(io, (int)wParam, false);
                return true;

            case Win32Native.WM_CHAR:
                if (wParam > 0 && wParam < 0x10000)
                {
                    io.AddInputCharacter((uint)wParam);
                }
                return true;

            case Win32Native.WM_SETFOCUS:
                io.AddFocusEvent(true);
                return false;

            case Win32Native.WM_KILLFOCUS:
                io.AddFocusEvent(false);
                return false;
        }

        return false;
    }

    private void CreateDeviceResources()
    {
        var creationFlags = DeviceCreationFlags.BgraSupport;
#if DEBUG
        creationFlags |= DeviceCreationFlags.Debug;
#endif

        var featureLevels = new[]
        {
            FeatureLevel.Level_11_0,
            FeatureLevel.Level_10_1,
            FeatureLevel.Level_10_0,
        };

        var result = Vortice.Direct3D11.D3D11.D3D11CreateDevice(
            IntPtr.Zero,
            DriverType.Hardware,
            creationFlags,
            featureLevels,
            out _device,
            out _,
            out _deviceContext);

        if (result.Failure || _device == null || _deviceContext == null)
        {
            throw new InvalidOperationException($"Could not create D3D11 device: {result.Code}");
        }

        using var factory = Vortice.DXGI.DXGI.CreateDXGIFactory1<IDXGIFactory2>();
        var swapChainDescription = new SwapChainDescription1(
            (uint)_width,
            (uint)_height,
            Format.R8G8B8A8_UNorm,
            false,
            Usage.RenderTargetOutput,
            2,
            Scaling.Stretch,
            SwapEffect.FlipDiscard,
            AlphaMode.Ignore,
            SwapChainFlags.None);

        using var swapChain1 = factory.CreateSwapChainForHwnd(_device, _hwnd, swapChainDescription, null, null);
        _swapChain = swapChain1.QueryInterface<IDXGISwapChain>();
        factory.MakeWindowAssociation(_hwnd, WindowAssociationFlags.IgnoreAltEnter);

        CreateRenderTarget();
    }

    private void CreateRenderTarget()
    {
        using var backBuffer = _swapChain!.GetBuffer<ID3D11Texture2D>(0);
        _renderTargetView = _device!.CreateRenderTargetView(backBuffer, null);
    }

    private void CreateDeviceObjects()
    {
        var vertexShaderBytecode = Compiler.Compile(VertexShaderSource, "main", "ImGuiVertexShader.hlsl", "vs_4_0", ShaderFlags.EnableStrictness, EffectFlags.None);
        var pixelShaderBytecode = Compiler.Compile(PixelShaderSource, "main", "ImGuiPixelShader.hlsl", "ps_4_0", ShaderFlags.EnableStrictness, EffectFlags.None);

        _vertexShader = _device!.CreateVertexShader(vertexShaderBytecode.Span);
        _pixelShader = _device!.CreatePixelShader(pixelShaderBytecode.Span);

        _inputLayout = _device.CreateInputLayout(
            new[]
            {
                new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 8, 0),
                new InputElementDescription("COLOR", 0, Format.R8G8B8A8_UNorm, 16, 0),
            },
            vertexShaderBytecode.Span);

        _vertexConstantBuffer = _device.CreateBuffer((uint)Unsafe.SizeOf<VertexConstantBuffer>(), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write);
        _vertexBuffer = _device.CreateBuffer((uint)(_vertexBufferSize * Unsafe.SizeOf<ImDrawVert>()), BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write);
        _indexBuffer = _device.CreateBuffer((uint)(_indexBufferSize * sizeof(ushort)), BindFlags.IndexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write);

        _blendState = _device.CreateBlendState(BlendDescription.NonPremultiplied);

        var rasterizerDescription = new RasterizerDescription
        {
            FillMode = FillMode.Solid,
            CullMode = CullMode.None,
            ScissorEnable = true,
            DepthClipEnable = true,
        };
        _rasterizerState = _device.CreateRasterizerState(rasterizerDescription);

        _depthStencilState = _device.CreateDepthStencilState(DepthStencilDescription.None);

        _fontSampler = _device.CreateSamplerState(SamplerDescription.LinearClamp);

        CreateFontTexture();
    }

    private void CreateFontTexture()
    {
        var io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out byte* pixels, out var width, out var height, out _);

        var textureDescription = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            ArraySize = 1,
            MipLevels = 1,
            Format = Format.R8G8B8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
        };

        var subresource = new SubresourceData((IntPtr)pixels, (uint)(width * 4), 0);
        using var texture = _device!.CreateTexture2D(textureDescription, subresource);
        _fontShaderResourceView = _device.CreateShaderResourceView(texture, null);

        _fontTextureId = RegisterTexture(_fontShaderResourceView);
        io.Fonts.SetTexID(_fontTextureId);
        io.Fonts.ClearTexData();
    }

    private nint RegisterTexture(ID3D11ShaderResourceView texture)
    {
        var textureId = _nextTextureId++;
        _textures[textureId] = texture;
        return textureId;
    }

    private void RenderDrawData(ImDrawDataPtr drawData)
    {
        if (_deviceContext == null || _renderTargetView == null || drawData.CmdListsCount == 0)
        {
            return;
        }

        if (drawData.TotalVtxCount <= 0 || drawData.TotalIdxCount <= 0)
        {
            return;
        }

        EnsureBuffers(drawData.TotalVtxCount, drawData.TotalIdxCount);
        if (!UploadBuffers(drawData))
        {
            return;
        }

        var left = drawData.DisplayPos.X;
        var right = drawData.DisplayPos.X + drawData.DisplaySize.X;
        var top = drawData.DisplayPos.Y;
        var bottom = drawData.DisplayPos.Y + drawData.DisplaySize.Y;

        var projection = new Matrix4x4(
            2.0f / (right - left), 0.0f, 0.0f, 0.0f,
            0.0f, 2.0f / (top - bottom), 0.0f, 0.0f,
            0.0f, 0.0f, 0.5f, 0.0f,
            (right + left) / (left - right), (top + bottom) / (bottom - top), 0.5f, 1.0f);

        var mappedConstantBuffer = _deviceContext.Map(_vertexConstantBuffer!, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        Unsafe.Write((void*)mappedConstantBuffer.DataPointer, new VertexConstantBuffer { ProjectionMatrix = projection });
        _deviceContext.Unmap(_vertexConstantBuffer!, 0);

        var viewport = new Viewport(0f, 0f, drawData.DisplaySize.X, drawData.DisplaySize.Y, 0f, 1f);
        _deviceContext.RSSetViewports(new[] { viewport });

        var blendFactor = stackalloc float[4];
        _deviceContext.OMSetBlendState(_blendState!, blendFactor, uint.MaxValue);
        _deviceContext.OMSetDepthStencilState(_depthStencilState!, 0);
        _deviceContext.RSSetState(_rasterizerState!);

        _deviceContext.IASetInputLayout(_inputLayout!);
        _deviceContext.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);

        var stride = (uint)Unsafe.SizeOf<ImDrawVert>();
        var offset = 0u;
        _deviceContext.IASetVertexBuffers(0, new[] { _vertexBuffer! }, new[] { stride }, new[] { offset });
        _deviceContext.IASetIndexBuffer(_indexBuffer!, Format.R16_UInt, 0);

        _deviceContext.VSSetShader(_vertexShader!, null, 0);
        _deviceContext.PSSetShader(_pixelShader!, null, 0);

        _deviceContext.VSSetConstantBuffers(0, new[] { _vertexConstantBuffer! });

        _deviceContext.PSSetSamplers(0, new[] { _fontSampler! });

        var clipOffset = drawData.DisplayPos;
        var globalVertexOffset = 0;
        var globalIndexOffset = 0;

        for (var n = 0; n < drawData.CmdListsCount; n++)
        {
            var commandList = drawData.CmdLists[n];

            for (var cmdIndex = 0; cmdIndex < commandList.CmdBuffer.Size; cmdIndex++)
            {
                var drawCommand = commandList.CmdBuffer[cmdIndex];

                if (drawCommand.UserCallback != IntPtr.Zero)
                {
                    continue;
                }

                var clipMinX = (int)(drawCommand.ClipRect.X - clipOffset.X);
                var clipMinY = (int)(drawCommand.ClipRect.Y - clipOffset.Y);
                var clipMaxX = (int)(drawCommand.ClipRect.Z - clipOffset.X);
                var clipMaxY = (int)(drawCommand.ClipRect.W - clipOffset.Y);

                if (clipMaxX <= clipMinX || clipMaxY <= clipMinY)
                {
                    continue;
                }

                var scissor = new RawRect(clipMinX, clipMinY, clipMaxX, clipMaxY);
                _deviceContext.RSSetScissorRects(new[] { scissor });

                var shaderResourceView = _textures.TryGetValue(drawCommand.TextureId, out var texture)
                    ? texture
                    : _fontShaderResourceView!;

                _deviceContext.PSSetShaderResources(0, new[] { shaderResourceView });

                _deviceContext.DrawIndexed(
                    (uint)drawCommand.ElemCount,
                    (uint)((int)drawCommand.IdxOffset + globalIndexOffset),
                    (int)drawCommand.VtxOffset + globalVertexOffset);
            }

            globalIndexOffset += commandList.IdxBuffer.Size;
            globalVertexOffset += commandList.VtxBuffer.Size;
        }
    }

    private void EnsureBuffers(int vertexCount, int indexCount)
    {
        if (vertexCount > _vertexBufferSize)
        {
            _vertexBuffer?.Dispose();
            _vertexBufferSize = vertexCount + 5000;
            _vertexBuffer = _device!.CreateBuffer((uint)(_vertexBufferSize * Unsafe.SizeOf<ImDrawVert>()), BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write);
        }

        if (indexCount > _indexBufferSize)
        {
            _indexBuffer?.Dispose();
            _indexBufferSize = indexCount + 10000;
            _indexBuffer = _device!.CreateBuffer((uint)(_indexBufferSize * sizeof(ushort)), BindFlags.IndexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write);
        }
    }

    private bool UploadBuffers(ImDrawDataPtr drawData)
    {
        if (_deviceContext == null || _vertexBuffer == null || _indexBuffer == null)
        {
            return false;
        }

        if (drawData.TotalVtxCount <= 0 || drawData.TotalIdxCount <= 0)
        {
            return false;
        }

        if (drawData.TotalVtxCount > _vertexBufferSize || drawData.TotalIdxCount > _indexBufferSize)
        {
            Debug.WriteLine($"Refusing to upload ImGui buffers larger than the allocated GPU buffers. Vertices: {drawData.TotalVtxCount}/{_vertexBufferSize}, indices: {drawData.TotalIdxCount}/{_indexBufferSize}.");
            return false;
        }

        var vertexMapped = false;
        var indexMapped = false;

        try
        {
            var mappedVertexBuffer = _deviceContext.Map(_vertexBuffer, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
            vertexMapped = true;
            var mappedIndexBuffer = _deviceContext.Map(_indexBuffer, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
            indexMapped = true;

            if (mappedVertexBuffer.DataPointer == IntPtr.Zero || mappedIndexBuffer.DataPointer == IntPtr.Zero)
            {
                Debug.WriteLine("Failed to map one or both ImGui upload buffers.");
                return false;
            }

            var vertexDestination = (ImDrawVert*)mappedVertexBuffer.DataPointer;
            var indexDestination = (ushort*)mappedIndexBuffer.DataPointer;
            var copiedVertexCount = 0;
            var copiedIndexCount = 0;

            for (var n = 0; n < drawData.CmdListsCount; n++)
            {
                var commandList = drawData.CmdLists[n];
                if (commandList.VtxBuffer.Size < 0 || commandList.IdxBuffer.Size < 0)
                {
                    Debug.WriteLine("Encountered an ImGui command list with an invalid negative buffer size.");
                    return false;
                }

                copiedVertexCount += commandList.VtxBuffer.Size;
                copiedIndexCount += commandList.IdxBuffer.Size;

                if (copiedVertexCount > drawData.TotalVtxCount || copiedIndexCount > drawData.TotalIdxCount)
                {
                    Debug.WriteLine("Refusing to upload ImGui buffers because the command list totals exceed the advertised draw data size.");
                    return false;
                }

                var vertexBytes = commandList.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>();
                var remainingVertexBytes = (drawData.TotalVtxCount - (copiedVertexCount - commandList.VtxBuffer.Size)) * Unsafe.SizeOf<ImDrawVert>();
                Buffer.MemoryCopy(commandList.VtxBuffer.Data.ToPointer(), vertexDestination, remainingVertexBytes, vertexBytes);

                var indexBytes = commandList.IdxBuffer.Size * sizeof(ushort);
                var remainingIndexBytes = (drawData.TotalIdxCount - (copiedIndexCount - commandList.IdxBuffer.Size)) * sizeof(ushort);
                Buffer.MemoryCopy(commandList.IdxBuffer.Data.ToPointer(), indexDestination, remainingIndexBytes, indexBytes);

                vertexDestination += commandList.VtxBuffer.Size;
                indexDestination += commandList.IdxBuffer.Size;
            }

            return true;
        }
        finally
        {
            if (vertexMapped)
            {
                _deviceContext.Unmap(_vertexBuffer, 0);
            }

            if (indexMapped)
            {
                _deviceContext.Unmap(_indexBuffer, 0);
            }
        }
    }

    private static void UpdateModifiers(ImGuiIOPtr io)
    {
        io.AddKeyEvent(ImGuiKey.ModCtrl, (Win32Native.GetKeyState(Win32Native.VK_CONTROL) & 0x8000) != 0);
        io.AddKeyEvent(ImGuiKey.ModShift, (Win32Native.GetKeyState(Win32Native.VK_SHIFT) & 0x8000) != 0);
        io.AddKeyEvent(ImGuiKey.ModAlt, (Win32Native.GetKeyState(Win32Native.VK_MENU) & 0x8000) != 0);
        io.AddKeyEvent(ImGuiKey.ModSuper, (Win32Native.GetKeyState(Win32Native.VK_LWIN) & 0x8000) != 0 || (Win32Native.GetKeyState(Win32Native.VK_RWIN) & 0x8000) != 0);
    }

    private static void AddKeyEvent(ImGuiIOPtr io, int virtualKey, bool isDown)
    {
        var key = MapKey(virtualKey);
        if (key != ImGuiKey.None)
        {
            io.AddKeyEvent(key, isDown);
        }
    }

    private static ImGuiKey MapKey(int virtualKey)
    {
        return virtualKey switch
        {
            Win32Native.VK_TAB => ImGuiKey.Tab,
            Win32Native.VK_LEFT => ImGuiKey.LeftArrow,
            Win32Native.VK_RIGHT => ImGuiKey.RightArrow,
            Win32Native.VK_UP => ImGuiKey.UpArrow,
            Win32Native.VK_DOWN => ImGuiKey.DownArrow,
            Win32Native.VK_PRIOR => ImGuiKey.PageUp,
            Win32Native.VK_NEXT => ImGuiKey.PageDown,
            Win32Native.VK_HOME => ImGuiKey.Home,
            Win32Native.VK_END => ImGuiKey.End,
            Win32Native.VK_INSERT => ImGuiKey.Insert,
            Win32Native.VK_DELETE => ImGuiKey.Delete,
            Win32Native.VK_BACK => ImGuiKey.Backspace,
            Win32Native.VK_SPACE => ImGuiKey.Space,
            Win32Native.VK_RETURN => ImGuiKey.Enter,
            Win32Native.VK_ESCAPE => ImGuiKey.Escape,
            Win32Native.VK_OEM_7 => ImGuiKey.Apostrophe,
            Win32Native.VK_OEM_COMMA => ImGuiKey.Comma,
            Win32Native.VK_OEM_MINUS => ImGuiKey.Minus,
            Win32Native.VK_OEM_PERIOD => ImGuiKey.Period,
            Win32Native.VK_OEM_2 => ImGuiKey.Slash,
            Win32Native.VK_OEM_1 => ImGuiKey.Semicolon,
            Win32Native.VK_OEM_PLUS => ImGuiKey.Equal,
            Win32Native.VK_OEM_4 => ImGuiKey.LeftBracket,
            Win32Native.VK_OEM_5 => ImGuiKey.Backslash,
            Win32Native.VK_OEM_6 => ImGuiKey.RightBracket,
            Win32Native.VK_OEM_3 => ImGuiKey.GraveAccent,
            Win32Native.VK_CAPITAL => ImGuiKey.CapsLock,
            Win32Native.VK_SCROLL => ImGuiKey.ScrollLock,
            Win32Native.VK_NUMLOCK => ImGuiKey.NumLock,
            Win32Native.VK_SNAPSHOT => ImGuiKey.PrintScreen,
            Win32Native.VK_PAUSE => ImGuiKey.Pause,
            Win32Native.VK_NUMPAD0 => ImGuiKey.Keypad0,
            Win32Native.VK_NUMPAD1 => ImGuiKey.Keypad1,
            Win32Native.VK_NUMPAD2 => ImGuiKey.Keypad2,
            Win32Native.VK_NUMPAD3 => ImGuiKey.Keypad3,
            Win32Native.VK_NUMPAD4 => ImGuiKey.Keypad4,
            Win32Native.VK_NUMPAD5 => ImGuiKey.Keypad5,
            Win32Native.VK_NUMPAD6 => ImGuiKey.Keypad6,
            Win32Native.VK_NUMPAD7 => ImGuiKey.Keypad7,
            Win32Native.VK_NUMPAD8 => ImGuiKey.Keypad8,
            Win32Native.VK_NUMPAD9 => ImGuiKey.Keypad9,
            Win32Native.VK_DECIMAL => ImGuiKey.KeypadDecimal,
            Win32Native.VK_DIVIDE => ImGuiKey.KeypadDivide,
            Win32Native.VK_MULTIPLY => ImGuiKey.KeypadMultiply,
            Win32Native.VK_SUBTRACT => ImGuiKey.KeypadSubtract,
            Win32Native.VK_ADD => ImGuiKey.KeypadAdd,
            Win32Native.VK_LSHIFT => ImGuiKey.LeftShift,
            Win32Native.VK_LCONTROL => ImGuiKey.LeftCtrl,
            Win32Native.VK_LMENU => ImGuiKey.LeftAlt,
            Win32Native.VK_LWIN => ImGuiKey.LeftSuper,
            Win32Native.VK_RSHIFT => ImGuiKey.RightShift,
            Win32Native.VK_RCONTROL => ImGuiKey.RightCtrl,
            Win32Native.VK_RMENU => ImGuiKey.RightAlt,
            Win32Native.VK_RWIN => ImGuiKey.RightSuper,
            Win32Native.VK_APPS => ImGuiKey.Menu,
            Win32Native.VK_F1 => ImGuiKey.F1,
            Win32Native.VK_F2 => ImGuiKey.F2,
            Win32Native.VK_F3 => ImGuiKey.F3,
            Win32Native.VK_F4 => ImGuiKey.F4,
            Win32Native.VK_F5 => ImGuiKey.F5,
            Win32Native.VK_F6 => ImGuiKey.F6,
            Win32Native.VK_F7 => ImGuiKey.F7,
            Win32Native.VK_F8 => ImGuiKey.F8,
            Win32Native.VK_F9 => ImGuiKey.F9,
            Win32Native.VK_F10 => ImGuiKey.F10,
            Win32Native.VK_F11 => ImGuiKey.F11,
            Win32Native.VK_F12 => ImGuiKey.F12,
            >= '0' and <= '9' => ImGuiKey._0 + (virtualKey - '0'),
            >= 'A' and <= 'Z' => ImGuiKey.A + (virtualKey - 'A'),
            _ => ImGuiKey.None,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VertexConstantBuffer
    {
        public Matrix4x4 ProjectionMatrix;
    }

}
