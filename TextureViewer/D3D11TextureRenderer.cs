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
    private uint channelMask = 0xFFFFFFFF; // All channels enabled by default
    private float currentGamma = 2.2f; // Default: sRGB gamma
    private float originalGamma = 2.2f; // Original gamma set during LoadTexture (for restoring after mask)

    // Histogram preprocessing compensation
    private HistogramMetadata? histogramMetadata = null;
    private bool enableHistogramCorrection = true; // Enabled by default if metadata present

    // Lock for thread-safe access to texture resources
    private readonly object renderLock = new object();

    [StructLayout(LayoutKind.Sequential)]
    private struct ShaderConstants {
        public Vector2 UvScale;
        public Vector2 UvOffset;
        public Vector2 PosScale;
        public float MipLevel;
        public float Exposure;
        public float Gamma;
        public uint ChannelMask;
        public Vector4 HistogramScale;  // RGB scale for histogram denormalization (w unused, for 16-byte alignment)
        public Vector4 HistogramOffset; // RGB offset for histogram denormalization (w unused, for 16-byte alignment)
        public uint EnableHistogramCorrection; // 0 = disabled, 1 = enabled
        public uint HistogramIsPerChannel; // 0 = scalar, 1 = per-channel
        public Vector2 Padding; // Padding to align to 16-byte boundary
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex {
        public Vector2 Position;
        public Vector2 TexCoord;
    }

    public bool IsInitialized => device != null;
    public int TextureWidth => currentTexture?.Width ?? 0;
    public int TextureHeight => currentTexture?.Height ?? 0;
    public int ViewportWidth => viewportWidth;
    public int ViewportHeight => viewportHeight;
    public int MipCount => currentTexture?.MipCount ?? 0;
    public bool IsSRGB => currentTexture?.IsSRGB ?? true; // Default to sRGB if no texture

    /// <summary>
    /// Initialize D3D11 device and resources.
    /// </summary>
    public void Initialize(IntPtr hwnd, int width, int height) {
        logger.Debug($"Initializing D3D11 renderer: {width}x{height}");

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
                Array.Empty<FeatureLevel>(),
                swapChainDesc,
                out swapChain,
                out device,
                out _,
                out context);

            CreateRenderTarget();
            CreateSamplers();
            CreateShaders();
            CreateVertexBuffer();
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
            logger.Error($"Shader file not found: {shaderPath}");
            logger.Error($"Current directory: {AppDomain.CurrentDomain.BaseDirectory}");
            logger.Error($"Working directory: {Directory.GetCurrentDirectory()}");

            // Try alternative path (in case HLSL is in root)
            shaderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TextureViewerShader.hlsl");
            if (File.Exists(shaderPath)) {
                logger.Debug($"Found shader at alternative path: {shaderPath}");
            } else {
                logger.Error($"Shader not found at alternative path either: {shaderPath}");
                logger.Warn("Rendering will NOT work without shaders!");
                return;
            }
        }

        try {
            string shaderCode = File.ReadAllText(shaderPath);

            // Compile vertex shader
            var vsBlob = Vortice.D3DCompiler.Compiler.Compile(
                shaderSource: shaderCode,
                entryPoint: "VSMain",
                sourceName: shaderPath,
                profile: "vs_5_0");

            if (vsBlob.IsEmpty) {
                logger.Error("Vertex shader compilation failed - empty bytecode");
                return;
            }

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
                sourceName: shaderPath,
                profile: "ps_5_0");

            if (psBlob.IsEmpty) {
                logger.Error("Pixel shader compilation failed - empty bytecode");
                return;
            }

            pixelShader = device!.CreatePixelShader(psBlob.Span);

            // Create constant buffer
            var cbDesc = new BufferDescription {
                ByteWidth = (uint)((Marshal.SizeOf<ShaderConstants>() + 15) & ~15), // Align to 16 bytes
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ConstantBuffer,
                CPUAccessFlags = CpuAccessFlags.Write
            };
            constantBuffer = device!.CreateBuffer(cbDesc);
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
        logger.Debug($"LoadTexture: {textureData.Width}x{textureData.Height}, {textureData.MipCount} mips");

        lock (renderLock) {
            currentTexture = textureData;

            // Store histogram metadata for GPU compensation
            histogramMetadata = textureData.HistogramMetadata;
            if (histogramMetadata != null) {
                logger.Info($"[D3D11TextureRenderer] Histogram metadata loaded: {histogramMetadata.Scale.Length} channel(s)");
                logger.Info($"[D3D11TextureRenderer] Scale = [{string.Join(", ", histogramMetadata.Scale.Select(s => s.ToString("F4")))}]");
                logger.Info($"[D3D11TextureRenderer] Offset = [{string.Join(", ", histogramMetadata.Offset.Select(o => o.ToString("F4")))}]");
                logger.Info($"[D3D11TextureRenderer] IsPerChannel = {histogramMetadata.IsPerChannel}");
                enableHistogramCorrection = true; // Enable by default when metadata present
            } else {
                logger.Info("[D3D11TextureRenderer] No histogram metadata in texture");
                enableHistogramCorrection = false; // No metadata, disable correction
            }

            // Set gamma based on texture format and type
            // Understanding:
            // - Monitor expects sRGB-encoded data
            // - Swapchain is R8G8B8A8_UNorm (no automatic sRGB conversion)
            // - Shader applies pow(color, 1/gamma) for encoding
            //
            // For PNG textures (loaded as R8G8B8A8_UNorm, no hardware sRGB decode):
            //   - IsSRGB=true (Albedo): PNG bytes are already sRGB-encoded
            //     -> gamma = 1.0 (pass through as-is, already correct for display)
            //   - IsSRGB=false (Normal/Roughness): PNG bytes are linear
            //     -> gamma = 2.2 (encode Linear->sRGB for display)
            //
            // For KTX2 textures:
            //   - BC7_UNorm_SRgb: Hardware decodes sRGB->Linear automatically
            //     -> gamma = 2.2 (re-encode Linear->sRGB for display)
            //   - BC7_UNorm: Hardware treats as linear
            //     -> gamma = 2.2 (encode Linear->sRGB for display)

            // Monitors expect sRGB-encoded data for correct display
            // Swapchain is R8G8B8A8_UNorm (no automatic sRGB conversion)
            // Shader applies: output = pow(input, 1/gamma)
            //
            // For PNG textures (loaded as R8G8B8A8_UNorm):
            //   - IsSRGB=true (Albedo): bytes are already sRGB-encoded
            //     → gamma=1.0 (pass-through, monitor expects sRGB)
            //   - IsSRGB=false (Normal): bytes are linear
            //     → gamma=2.2 (encode Linear→sRGB for monitor)
            //
            // For KTX2 textures:
            //   - BC7_UNorm_SRgb: GPU decodes sRGB→Linear automatically
            //     → gamma=2.2 (re-encode Linear→sRGB for monitor)
            //   - BC7_UNorm: GPU treats as Linear
            //     → gamma=2.2 (encode Linear→sRGB for monitor)

            if (textureData.SourceFormat.Contains("PNG")) {
                // PNG: Check IsSRGB flag to determine if bytes are already sRGB-encoded
                currentGamma = textureData.IsSRGB ? 1.0f : 2.2f;
                originalGamma = currentGamma; // Save for restoring after mask
            } else {
                // KTX2: GPU output is always Linear, need to encode to sRGB for monitor
                currentGamma = 2.2f;
                originalGamma = currentGamma; // Save for restoring after mask
            }

            // Dispose old texture
            textureSRV?.Dispose();
            texture?.Dispose();

        // Determine format based on compression
        Format format;
        if (textureData.IsCompressed && textureData.CompressionFormat != null) {
            // Block-compressed format
            format = textureData.CompressionFormat switch {
                "BC7_SRGB_BLOCK" => Format.BC7_UNorm_SRgb,
                "BC7_UNORM_BLOCK" => Format.BC7_UNorm,
                "BC3_SRGB_BLOCK" => Format.BC3_UNorm_SRgb,
                "BC3_UNORM_BLOCK" => Format.BC3_UNorm,
                "BC1_RGBA_SRGB_BLOCK" => Format.BC1_UNorm_SRgb,
                "BC1_RGBA_UNORM_BLOCK" => Format.BC1_UNorm,
                _ => throw new Exception($"Unsupported compression format: {textureData.CompressionFormat}")
            };
        } else {
            // Uncompressed RGBA8 - WPF gives us BGRA32, so use BGRA format
            // Always use UNorm (no automatic sRGB decode)
            // Gamma correction in shader will handle sRGB encoding for display
            format = Format.B8G8R8A8_UNorm;
        }

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

                if (mip.Data == null || mip.Data.Length == 0) {
                    throw new Exception($"Mip {i} has no data!");
                }

                pinnedHandles[i] = GCHandle.Alloc(mip.Data, GCHandleType.Pinned);

                // For 2D textures, SlicePitch is the total size of the mip level
                // For block-compressed formats, this is crucial
                uint slicePitch = (uint)mip.Data.Length;

                subresources[i] = new SubresourceData {
                    DataPointer = pinnedHandles[i].AddrOfPinnedObject(),
                    RowPitch = (uint)mip.RowPitch,
                    SlicePitch = slicePitch
                };
            }

            // Create texture
            texture = device!.CreateTexture2D(texDesc, subresources);

            // Create SRV
            textureSRV = device!.CreateShaderResourceView(texture);
        } finally {
            // Free pinned handles
            foreach (var handle in pinnedHandles) {
                if (handle.IsAllocated) {
                    handle.Free();
                }
            }
        }

            // Don't reset view state - preserve zoom/pan when switching textures
            // Only reset mip level since the new texture might have different mip count
            currentMipLevel = 0;
            // Keep existing zoom and pan values
            // zoomLevel = 1.0f;  // DON'T RESET
            // panX = 0.0f;        // DON'T RESET
            // panY = 0.0f;        // DON'T RESET
        } // lock (renderLock)
    }

    /// <summary>
    /// Render the current texture.
    /// </summary>
    public void Render() {
        if (context == null || renderTargetView == null) {
            return;
        }

        // Clear background to dark gray
        context.ClearRenderTargetView(renderTargetView, new Color4(0.2f, 0.2f, 0.2f, 1.0f));

        lock (renderLock) {
            // If no texture loaded, just present the cleared background
            if (textureSRV == null) {
                swapChain!.Present(1, PresentFlags.None);
                return;
            }

            // Set render target
            context.OMSetRenderTargets(renderTargetView);

            // Set viewport
            var viewport = new Viewport(0, 0, viewportWidth, viewportHeight);
            context.RSSetViewport(viewport);

            // Check if shaders are available
            if (vertexShader == null || pixelShader == null || inputLayout == null || constantBuffer == null || vertexBuffer == null) {
                logger.Error("Cannot render: shaders not initialized");
                swapChain!.Present(1, PresentFlags.None);
                return;
            }

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
        } // lock (renderLock)
    }

    private void UpdateConstantBuffer() {
        // Calculate aspect ratio correction
        float viewportAspect = (float)viewportWidth / viewportHeight;
        float textureAspect = currentTexture != null ? (float)currentTexture.Width / currentTexture.Height : 1.0f;

        Vector2 posScale;
        if (viewportAspect > textureAspect) {
            // Viewport is wider than texture - fit by height (pillarbox)
            posScale = new Vector2(textureAspect / viewportAspect, 1.0f);
        } else {
            // Viewport is taller than texture - fit by width (letterbox)
            posScale = new Vector2(1.0f, viewportAspect / textureAspect);
        }

        // Prepare histogram metadata for shader (using Vector4 for proper HLSL alignment)
        Vector4 histScale = new Vector4(1.0f, 1.0f, 1.0f, 0.0f);
        Vector4 histOffset = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
        uint enableHist = 0;
        uint isPerChannel = 0;

        if (histogramMetadata != null && enableHistogramCorrection) {
            enableHist = 1;
            isPerChannel = histogramMetadata.IsPerChannel ? 1u : 0u;

            if (histogramMetadata.IsPerChannel) {
                // Per-channel: use RGB values
                histScale = new Vector4(
                    histogramMetadata.Scale[0],
                    histogramMetadata.Scale.Length > 1 ? histogramMetadata.Scale[1] : histogramMetadata.Scale[0],
                    histogramMetadata.Scale.Length > 2 ? histogramMetadata.Scale[2] : histogramMetadata.Scale[0],
                    0.0f  // w unused
                );
                histOffset = new Vector4(
                    histogramMetadata.Offset[0],
                    histogramMetadata.Offset.Length > 1 ? histogramMetadata.Offset[1] : histogramMetadata.Offset[0],
                    histogramMetadata.Offset.Length > 2 ? histogramMetadata.Offset[2] : histogramMetadata.Offset[0],
                    0.0f  // w unused
                );
            } else {
                // Scalar: use same value for all channels
                float scale = histogramMetadata.Scale[0];
                float offset = histogramMetadata.Offset[0];
                histScale = new Vector4(scale, scale, scale, 0.0f);
                histOffset = new Vector4(offset, offset, offset, 0.0f);
            }
        }

        var constants = new ShaderConstants {
            UvScale = new Vector2(1.0f / zoomLevel, 1.0f / zoomLevel),
            UvOffset = new Vector2(panX, panY),
            PosScale = posScale,
            MipLevel = currentMipLevel,
            Exposure = 0.0f,
            Gamma = currentGamma,
            ChannelMask = channelMask,
            HistogramScale = histScale,
            HistogramOffset = histOffset,
            EnableHistogramCorrection = enableHist,
            HistogramIsPerChannel = isPerChannel,
            Padding = new Vector2(0.0f, 0.0f)
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
    public void SetZoom(float zoom) {
        lock (renderLock) {
            zoomLevel = Math.Clamp(zoom, 0.125f, 16.0f);
        }
    }

    public void SetPan(float x, float y) {
        lock (renderLock) {
            panX = x;
            panY = y;
        }
    }

    public void SetMipLevel(int level) {
        lock (renderLock) {
            currentMipLevel = Math.Clamp(level, 0, Math.Max(0, MipCount - 1));
        }
    }

    public void SetChannelMask(uint mask) {
        lock (renderLock) {
            channelMask = mask;
        }
    }

    public void SetGamma(float gamma) {
        lock (renderLock) {
            currentGamma = gamma;
        }
    }

    public void RestoreOriginalGamma() {
        lock (renderLock) {
            currentGamma = originalGamma;
        }
    }

    public void SetFilter(bool linear) {
        lock (renderLock) {
            useLinearFilter = linear;
        }
    }

    public void SetHistogramCorrection(bool enabled) {
        lock (renderLock) {
            enableHistogramCorrection = enabled;
            if (histogramMetadata != null) {
                logger.Info($"[D3D11TextureRenderer] Histogram correction {(enabled ? "ENABLED" : "DISABLED")}");
                if (enabled) {
                    if (histogramMetadata.IsPerChannel && histogramMetadata.Scale.Length >= 3) {
                        // Per-channel mode: show all RGB channels
                        logger.Info($"[D3D11TextureRenderer] Per-channel mode (R, G, B):");
                        logger.Info($"  R: v_original = v_normalized * {histogramMetadata.Scale[0]:F4} + {histogramMetadata.Offset[0]:F4}");
                        logger.Info($"  G: v_original = v_normalized * {histogramMetadata.Scale[1]:F4} + {histogramMetadata.Offset[1]:F4}");
                        logger.Info($"  B: v_original = v_normalized * {histogramMetadata.Scale[2]:F4} + {histogramMetadata.Offset[2]:F4}");
                    } else {
                        // Scalar mode: show single value for all channels
                        logger.Info($"[D3D11TextureRenderer] Scalar mode (all channels):");
                        logger.Info($"  v_original = v_normalized * {histogramMetadata.Scale[0]:F4} + {histogramMetadata.Offset[0]:F4}");
                    }
                }
            } else {
                logger.Info($"[D3D11TextureRenderer] SetHistogramCorrection called but NO metadata (enabled={enabled})");
            }
        }
    }

    public bool GetHistogramCorrection() {
        lock (renderLock) {
            return enableHistogramCorrection;
        }
    }

    public bool HasHistogramMetadata() {
        lock (renderLock) {
            return histogramMetadata != null;
        }
    }

    public void ResetView() {
        lock (renderLock) {
            zoomLevel = 1.0f;
            panX = 0.0f;
            panY = 0.0f;
            currentMipLevel = 0;
        }
    }

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

        logger.Debug("D3D11 renderer disposed");
    }
}
