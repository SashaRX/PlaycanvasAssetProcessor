using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using NLog;

namespace AssetProcessor.TextureViewer;

/// <summary>
/// D3D11 renderer for texture viewing with zoom/pan/mip control.
/// Simplified version compatible with Vortice v3.6.2.
/// </summary>
public sealed class D3D11TextureRenderer : IDisposable {
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();

    private ID3D11Device? device;
    private ID3D11DeviceContext? context;
    private IDXGISwapChain? swapChain;
    private ID3D11RenderTargetView? renderTargetView;

    private ID3D11Texture2D? texture;
    private ID3D11ShaderResourceView? textureSRV;
    private ID3D11SamplerState? samplerPoint;
    private ID3D11SamplerState? samplerLinear;

    private ID3D11VertexShader? vertexShader;
    private ID3D11PixelShader? pixelShader;
    private ID3D11Buffer? constantBuffer;
    private ID3D11Buffer? vertexBuffer;
    private ID3D11InputLayout? inputLayout;

    private IntPtr windowHandle;
    private int viewportWidth;
    private int viewportHeight;

    private TextureData? currentTexture;
    private bool useLinearFilter = true;
    private int currentMipLevel = 0;
    private float zoomLevel = 1.0f;
    private float panX = 0.0f;
    private float panY = 0.0f;

    [StructLayout(LayoutKind.Sequential)]
    private struct ShaderConstants {
        public Vector2 UvScale;
        public Vector2 UvOffset;
        public float MipLevel;
        public float Exposure;
        public float Gamma;
        public uint ChannelMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex {
        public Vector2 Position;
        public Vector2 TexCoord;
    }

    public bool IsInitialized => device != null;
    public int TextureWidth => currentTexture?.Width ?? 0;
    public int TextureHeight => currentTexture?.Height ?? 0;
    public int MipCount => currentTexture?.MipCount ?? 0;

    /// <summary>
    /// Initialize D3D11 device and resources.
    /// </summary>
    public void Initialize(IntPtr hwnd, int width, int height) {
        logger.Info($"Initializing D3D11 renderer: {width}x{height}, hwnd={hwnd:X}");

        windowHandle = hwnd;
        viewportWidth = width;
        viewportHeight = height;

        try {
            // Create swap chain description
            var swapChainDesc = new SwapChainDescription {
                BufferCount = 2,
                BufferDescription = new ModeDescription {
                    Width = (uint)width,
                    Height = (uint)height,
                    Format = Format.R8G8B8A8_UNorm,
                    RefreshRate = new Rational(60, 1)
                },
                OutputWindow = hwnd,
                SampleDescription = new SampleDescription(1, 0),
                BufferUsage = Usage.RenderTargetOutput,
                SwapEffect = SwapEffect.FlipDiscard,
                Windowed = true
            };

            // Create device and swap chain
            D3D11.D3D11CreateDeviceAndSwapChain(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                null,
                swapChainDesc,
                out swapChain,
                out device,
                out _,
                out context);

            logger.Info("D3D11 device created successfully");

            CreateRenderTarget();
            CreateSamplers();
            CreateShaders();
            CreateVertexBuffer();

            logger.Info("D3D11 renderer initialized successfully");
        } catch (Exception ex) {
            logger.Error(ex, "Failed to initialize D3D11 renderer");
            throw;
        }
    }

    private void CreateRenderTarget() {
        using var backBuffer = swapChain!.GetBuffer<ID3D11Texture2D>(0);
        renderTargetView = device!.CreateRenderTargetView(backBuffer);
    }

    private void CreateSamplers() {
        // Point sampling
        var pointDesc = new SamplerDescription {
            Filter = Filter.MinMagMipPoint,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            MinLOD = 0,
            MaxLOD = float.MaxValue
        };
        samplerPoint = device!.CreateSamplerState(pointDesc);

        // Linear sampling
        var linearDesc = new SamplerDescription {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            MinLOD = 0,
            MaxLOD = float.MaxValue
        };
        samplerLinear = device!.CreateSamplerState(linearDesc);
    }

    private void CreateShaders() {
        string shaderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TextureViewer", "TextureViewerShader.hlsl");

        if (!File.Exists(shaderPath)) {
            logger.Warn($"Shader file not found: {shaderPath}, using fallback");
            // For now, we'll skip shader creation and render without shaders
            // TODO: Embed shaders as resources or generate them at runtime
            return;
        }

        try {
            string shaderCode = File.ReadAllText(shaderPath);

            // Compile vertex shader
            var vsBlob = Vortice.D3DCompiler.Compiler.Compile(
                shaderSource: shaderCode,
                entryPoint: "VSMain",
                sourceName: null,
                profile: "vs_5_0");

            vertexShader = device!.CreateVertexShader(vsBlob.Span);

            // Create input layout
            var inputElements = new InputElementDescription[] {
                new("POSITION", 0, Format.R32G32_Float, 0, 0),
                new("TEXCOORD", 0, Format.R32G32_Float, 8, 0)
            };
            inputLayout = device!.CreateInputLayout(inputElements, vsBlob.Span);

            // Compile pixel shader
            var psBlob = Vortice.D3DCompiler.Compiler.Compile(
                shaderSource: shaderCode,
                entryPoint: "PSMain",
                sourceName: null,
                profile: "ps_5_0");

            pixelShader = device!.CreatePixelShader(psBlob.Span);

            // Create constant buffer
            var cbDesc = new BufferDescription {
                ByteWidth = (uint)((Marshal.SizeOf<ShaderConstants>() + 15) & ~15), // Align to 16 bytes
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ConstantBuffer,
                CPUAccessFlags = CpuAccessFlags.Write
            };
            constantBuffer = device!.CreateBuffer(cbDesc);

            logger.Info("Shaders compiled and created successfully");
        } catch (Exception ex) {
            logger.Error(ex, "Failed to compile shaders");
        }
    }

    private void CreateVertexBuffer() {
        var vertices = new Vertex[] {
            new() { Position = new(-1, -1), TexCoord = new(0, 1) },
            new() { Position = new(-1,  1), TexCoord = new(0, 0) },
            new() { Position = new( 1,  1), TexCoord = new(1, 0) },
            new() { Position = new(-1, -1), TexCoord = new(0, 1) },
            new() { Position = new( 1,  1), TexCoord = new(1, 0) },
            new() { Position = new( 1, -1), TexCoord = new(1, 1) }
        };

        int vertexSize = Marshal.SizeOf<Vertex>();
        int bufferSize = vertexSize * vertices.Length;

        var vbDesc = new BufferDescription {
            ByteWidth = (uint)bufferSize,
            Usage = ResourceUsage.Immutable,
            BindFlags = BindFlags.VertexBuffer
        };

        // Copy vertex data to unmanaged memory
        IntPtr dataPtr = Marshal.AllocHGlobal(bufferSize);
        try {
            for (int i = 0; i < vertices.Length; i++) {
                Marshal.StructureToPtr(vertices[i], IntPtr.Add(dataPtr, i * vertexSize), false);
            }

            var subresource = new SubresourceData {
                DataPointer = dataPtr,
                RowPitch = 0,
                SlicePitch = 0
            };

            vertexBuffer = device!.CreateBuffer(vbDesc, subresource);
        } finally {
            Marshal.FreeHGlobal(dataPtr);
        }
    }

    /// <summary>
    /// Load texture data into GPU.
    /// </summary>
    public void LoadTexture(TextureData textureData) {
        logger.Info($"Loading texture: {textureData.Width}x{textureData.Height}, {textureData.MipCount} mips");

        currentTexture = textureData;

        // Dispose old texture
        textureSRV?.Dispose();
        texture?.Dispose();

        // Determine format
        Format format = textureData.IsSRGB ? Format.R8G8B8A8_UNorm_SRgb : Format.R8G8B8A8_UNorm;

        // Create texture description
        var texDesc = new Texture2DDescription {
            Width = (uint)textureData.Width,
            Height = (uint)textureData.Height,
            MipLevels = (uint)textureData.MipCount,
            ArraySize = 1,
            Format = format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Immutable,
            BindFlags = BindFlags.ShaderResource
        };

        // Prepare subresource data
        var subresources = new SubresourceData[textureData.MipCount];
        var pinnedHandles = new GCHandle[textureData.MipCount];

        try {
            for (int i = 0; i < textureData.MipCount; i++) {
                var mip = textureData.MipLevels[i];
                pinnedHandles[i] = GCHandle.Alloc(mip.Data, GCHandleType.Pinned);

                subresources[i] = new SubresourceData {
                    DataPointer = pinnedHandles[i].AddrOfPinnedObject(),
                    RowPitch = (uint)mip.RowPitch,
                    SlicePitch = 0
                };
            }

            // Create texture
            texture = device!.CreateTexture2D(texDesc, subresources);

            // Create SRV
            textureSRV = device!.CreateShaderResourceView(texture);

            logger.Info("Texture loaded successfully");
        } finally {
            // Free pinned handles
            foreach (var handle in pinnedHandles) {
                if (handle.IsAllocated) {
                    handle.Free();
                }
            }
        }

        // Reset view state
        currentMipLevel = 0;
        zoomLevel = 1.0f;
        panX = 0.0f;
        panY = 0.0f;
    }

    /// <summary>
    /// Render the current texture.
    /// </summary>
    public void Render() {
        if (context == null || renderTargetView == null || textureSRV == null) {
            return;
        }

        // Clear background
        context.ClearRenderTargetView(renderTargetView, new Color4(0.2f, 0.2f, 0.2f, 1.0f));

        // Set render target
        context.OMSetRenderTargets(renderTargetView);

        // Set viewport
        var viewport = new Viewport(0, 0, viewportWidth, viewportHeight);
        context.RSSetViewport(viewport);

        if (vertexShader != null && pixelShader != null && inputLayout != null && constantBuffer != null && vertexBuffer != null) {
            // Update constant buffer
            UpdateConstantBuffer();

            // Set shaders and resources
            context.VSSetShader(vertexShader);
            context.PSSetShader(pixelShader);
            context.PSSetShaderResource(0, textureSRV);
            context.PSSetSampler(0, useLinearFilter ? samplerLinear : samplerPoint);
            context.VSSetConstantBuffer(0, constantBuffer);
            context.PSSetConstantBuffer(0, constantBuffer);

            // Set input layout and vertex buffer
            context.IASetInputLayout(inputLayout);
            context.IASetVertexBuffer(0, vertexBuffer, (uint)Marshal.SizeOf<Vertex>(), 0);
            context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);

            // Draw
            context.Draw(6, 0);
        }

        // Present
        swapChain!.Present(1, PresentFlags.None);
    }

    private void UpdateConstantBuffer() {
        var constants = new ShaderConstants {
            UvScale = new Vector2(1.0f / zoomLevel, 1.0f / zoomLevel),
            UvOffset = new Vector2(panX, panY),
            MipLevel = currentMipLevel,
            Exposure = 0.0f,
            Gamma = 2.2f,
            ChannelMask = 0xFFFFFFFF
        };

        var mapped = context!.Map(constantBuffer!, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        Marshal.StructureToPtr(constants, mapped.DataPointer, false);
        context.Unmap(constantBuffer!, 0);
    }

    public void Resize(int width, int height) {
        if (width <= 0 || height <= 0) return;

        viewportWidth = width;
        viewportHeight = height;

        renderTargetView?.Dispose();
        swapChain?.ResizeBuffers(2, (uint)width, (uint)height, Format.R8G8B8A8_UNorm, SwapChainFlags.None);
        CreateRenderTarget();
    }

    // Public control methods
    public void SetZoom(float zoom) => zoomLevel = Math.Clamp(zoom, 0.125f, 16.0f);
    public void SetPan(float x, float y) { panX = x; panY = y; }
    public void SetMipLevel(int level) => currentMipLevel = Math.Clamp(level, 0, Math.Max(0, MipCount - 1));
    public void SetFilter(bool linear) => useLinearFilter = linear;

    public void Dispose() {
        textureSRV?.Dispose();
        texture?.Dispose();
        samplerPoint?.Dispose();
        samplerLinear?.Dispose();
        vertexShader?.Dispose();
        pixelShader?.Dispose();
        constantBuffer?.Dispose();
        vertexBuffer?.Dispose();
        inputLayout?.Dispose();
        renderTargetView?.Dispose();
        swapChain?.Dispose();
        context?.Dispose();
        device?.Dispose();

        logger.Info("D3D11 renderer disposed");
    }
}
