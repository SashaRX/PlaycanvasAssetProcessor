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
    private ID3D11SamplerState? samplerAniso;

    private ID3D11VertexShader? vertexShader;
    private ID3D11PixelShader? pixelShader;
    private ID3D11Buffer? constantBuffer;
    private ID3D11Buffer? vertexBuffer;
    private ID3D11InputLayout? inputLayout;

    private IntPtr windowHandle;
    private int viewportWidth;
    private int viewportHeight;

    private TextureData? currentTexture;
    private bool useSRGB = true;
    private bool useLinearFilter = true;
    private bool useAnisotropic = false;
    private int currentMipLevel = 0; // -1 = auto, 0+ = fixed mip
    private float exposure = 0.0f;
    private uint channelMask = 0xFFFFFFFF; // All channels

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

        // Create device and swap chain
        var swapChainDesc = new SwapChainDescription {
            BufferDescription = new ModeDescription(width, height, Format.R8G8B8A8_UNorm),
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            OutputWindow = hwnd,
            IsWindowed = true,
            SwapEffect = SwapEffect.FlipDiscard,
            Flags = SwapChainFlags.None
        };

        D3D11.D3D11CreateDeviceAndSwapChain(
            null,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            null,
            swapChainDesc,
            out swapChain,
            out device,
            out var featureLevel,
            out context);

        logger.Info($"D3D11 device created: FeatureLevel={featureLevel}");

        CreateRenderTarget();
        CreateSamplers();
        CreateShaders();
        CreateVertexBuffer();

        logger.Info("D3D11 renderer initialized successfully");
    }

    private void CreateRenderTarget() {
        using var backBuffer = swapChain!.GetBuffer<ID3D11Texture2D>(0);
        renderTargetView = device!.CreateRenderTargetView(backBuffer);
    }

    private void CreateSamplers() {
        // Point sampling (nearest neighbor)
        samplerPoint = device!.CreateSamplerState(new SamplerDescription {
            Filter = Filter.MinMagMipPoint,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            ComparisonFunction = ComparisonFunction.Never,
            MinLOD = 0,
            MaxLOD = float.MaxValue
        });

        // Linear (bilinear) sampling
        samplerLinear = device!.CreateSamplerState(new SamplerDescription {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            ComparisonFunction = ComparisonFunction.Never,
            MinLOD = 0,
            MaxLOD = float.MaxValue
        });

        // Anisotropic sampling
        samplerAniso = device!.CreateSamplerState(new SamplerDescription {
            Filter = Filter.Anisotropic,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            ComparisonFunction = ComparisonFunction.Never,
            MaxAnisotropy = 16,
            MinLOD = 0,
            MaxLOD = float.MaxValue
        });
    }

    private void CreateShaders() {
        // Compile shaders from embedded resource or file
        string shaderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TextureViewer", "TextureViewerShader.hlsl");

        if (!File.Exists(shaderPath)) {
            logger.Error($"Shader file not found: {shaderPath}");
            throw new FileNotFoundException("Shader file not found", shaderPath);
        }

        string shaderCode = File.ReadAllText(shaderPath);

        // Compile vertex shader
        var vsBlob = Vortice.D3DCompiler.Compiler.Compile(shaderCode, "VSMain", "vs_5_0");
        vertexShader = device!.CreateVertexShader(vsBlob);

        // Compile pixel shader
        var psBlob = Vortice.D3DCompiler.Compiler.Compile(shaderCode, "PSMain", "ps_5_0");
        pixelShader = device!.CreatePixelShader(psBlob);

        // Create input layout
        var inputElements = new InputElementDescription[] {
            new("POSITION", 0, Format.R32G32_Float, 0, 0),
            new("TEXCOORD", 0, Format.R32G32_Float, 8, 0)
        };
        inputLayout = device!.CreateInputLayout(inputElements, vsBlob);

        // Create constant buffer
        constantBuffer = device!.CreateBuffer(new BufferDescription {
            ByteWidth = Marshal.SizeOf<ShaderConstants>(),
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ConstantBuffer,
            CPUAccessFlags = CpuAccessFlags.Write
        });
    }

    private void CreateVertexBuffer() {
        // Full-screen quad: two triangles covering [-1, 1] NDC space
        var vertices = new Vertex[] {
            new() { Position = new(-1, -1), TexCoord = new(0, 1) }, // Bottom-left
            new() { Position = new(-1,  1), TexCoord = new(0, 0) }, // Top-left
            new() { Position = new( 1,  1), TexCoord = new(1, 0) }, // Top-right

            new() { Position = new(-1, -1), TexCoord = new(0, 1) }, // Bottom-left
            new() { Position = new( 1,  1), TexCoord = new(1, 0) }, // Top-right
            new() { Position = new( 1, -1), TexCoord = new(1, 1) }  // Bottom-right
        };

        vertexBuffer = device!.CreateBuffer(vertices, new BufferDescription {
            ByteWidth = Marshal.SizeOf<Vertex>() * vertices.Length,
            Usage = ResourceUsage.Immutable,
            BindFlags = BindFlags.VertexBuffer
        });
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

        // Determine format (sRGB or linear)
        Format format = textureData.IsSRGB ? Format.R8G8B8A8_UNorm_SRgb : Format.R8G8B8A8_UNorm;

        // Create texture description
        var texDesc = new Texture2DDescription {
            Width = textureData.Width,
            Height = textureData.Height,
            MipLevels = textureData.MipCount,
            ArraySize = 1,
            Format = format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Immutable,
            BindFlags = BindFlags.ShaderResource
        };

        // Prepare subresource data for all mip levels
        var subresources = new SubresourceData[textureData.MipCount];
        for (int i = 0; i < textureData.MipCount; i++) {
            var mip = textureData.MipLevels[i];
            subresources[i] = new SubresourceData {
                DataPointer = Marshal.UnsafeAddrOfPinnedArrayElement(mip.Data, 0),
                RowPitch = mip.RowPitch,
                SlicePitch = 0
            };
        }

        // Create texture
        texture = device!.CreateTexture2D(texDesc, subresources);

        // Create SRV
        textureSRV = device!.CreateShaderResourceView(texture);

        // Reset view state
        currentMipLevel = 0;
        zoomLevel = 1.0f;
        panX = 0.0f;
        panY = 0.0f;

        logger.Info("Texture loaded successfully");
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
        context.RSSetViewport(0, 0, viewportWidth, viewportHeight);

        // Update constant buffer
        UpdateConstantBuffer();

        // Set shaders and resources
        context.VSSetShader(vertexShader);
        context.PSSetShader(pixelShader);
        context.PSSetShaderResource(0, textureSRV);
        context.PSSetSampler(0, GetCurrentSampler());
        context.VSSetConstantBuffer(0, constantBuffer);
        context.PSSetConstantBuffer(0, constantBuffer);

        // Set input layout and vertex buffer
        context.IASetInputLayout(inputLayout);
        context.IASetVertexBuffer(0, vertexBuffer, Marshal.SizeOf<Vertex>());
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);

        // Draw
        context.Draw(6, 0);

        // Present
        swapChain!.Present(1, PresentFlags.None);
    }

    private void UpdateConstantBuffer() {
        var constants = new ShaderConstants {
            UvScale = new Vector2(1.0f / zoomLevel, 1.0f / zoomLevel),
            UvOffset = new Vector2(panX, panY),
            MipLevel = currentMipLevel,
            Exposure = exposure,
            Gamma = useSRGB ? 2.2f : 1.0f,
            ChannelMask = channelMask
        };

        var mappedResource = context!.Map(constantBuffer, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        Marshal.StructureToPtr(constants, mappedResource.DataPointer, false);
        context.Unmap(constantBuffer, 0);
    }

    private ID3D11SamplerState GetCurrentSampler() {
        if (useAnisotropic) return samplerAniso!;
        if (useLinearFilter) return samplerLinear!;
        return samplerPoint!;
    }

    public void Resize(int width, int height) {
        if (width <= 0 || height <= 0) return;

        viewportWidth = width;
        viewportHeight = height;

        renderTargetView?.Dispose();

        swapChain?.ResizeBuffers(2, width, height, Format.R8G8B8A8_UNorm, SwapChainFlags.None);

        CreateRenderTarget();
    }

    // Public properties for control
    public void SetZoom(float zoom) => zoomLevel = Math.Clamp(zoom, 0.125f, 16.0f);
    public void SetPan(float x, float y) { panX = x; panY = y; }
    public void SetMipLevel(int level) => currentMipLevel = Math.Clamp(level, -1, MipCount - 1);
    public void SetFilter(bool linear, bool aniso) { useLinearFilter = linear; useAnisotropic = aniso; }
    public void SetSRGB(bool enabled) => useSRGB = enabled;
    public void SetExposure(float ev) => exposure = ev;
    public void SetChannelMask(uint mask) => channelMask = mask;

    public void Dispose() {
        textureSRV?.Dispose();
        texture?.Dispose();
        samplerPoint?.Dispose();
        samplerLinear?.Dispose();
        samplerAniso?.Dispose();
        vertexShader?.Dispose();
        pixelShader?.Dispose();
        constantBuffer?.Dispose();
        vertexBuffer?.Dispose();
        inputLayout?.Dispose();
        renderTargetView?.Dispose();
        swapChain?.Dispose();
        context?.Dispose();
        device?.Dispose();
    }
}
