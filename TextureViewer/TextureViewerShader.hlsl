// Texture Viewer Shader
// Supports: sRGB/Linear toggle, manual mip level, HDR exposure, channel swizzle

cbuffer ShaderConstants : register(b0)
{
    float2 uvScale;                     // UV scaling for zoom
    float2 uvOffset;                    // UV offset for pan
    float2 posScale;                    // Position scaling (kept for compatibility, now set to 1)
    float mipLevel;                     // Manual mip level (-1 for auto)
    float exposure;                     // HDR exposure (EV)
    float gamma;                        // Gamma correction (2.2 for sRGB, 1.0 for linear)
    uint channelMask;                   // RGBA channel mask (bit flags)
    // CRITICAL: HLSL adds implicit padding here (8 bytes) to align float4 to 16-byte boundary
    // C# struct must have explicit Vector2 Padding1 field to match this alignment
    float4 histogramScale;              // Histogram denormalization scale (RGB, w unused)
    float4 histogramOffset;             // Histogram denormalization offset (RGB, w unused)
    uint enableHistogramCorrection;     // 0 = disabled, 1 = enabled
    uint histogramIsPerChannel;         // 0 = scalar, 1 = per-channel
    uint normalLayout;                  // Normal map layout: 0=NONE, 1=RG, 2=GA, 3=RGB, 4=AG, 5=RGBxAy
    uint enableTiling;                  // 0 = clamp to [0,1], 1 = tile UVs
};

Texture2D<float4> sourceTexture : register(t0);
SamplerState texSampler : register(s0);

struct VSInput
{
    float2 position : POSITION;
    float2 texcoord : TEXCOORD0;
};

struct PSInput
{
    float4 position : SV_POSITION;
    float2 texcoord : TEXCOORD0;
    float2 originalUV : TEXCOORD1; // Original UV before zoom/pan
};

// Vertex Shader: Full-screen quad
PSInput VSMain(VSInput input)
{
    PSInput output;

    // Pass through quad vertices (posScale stays at 1 to cover the full viewport)
    float2 pos = input.position * posScale;
    output.position = float4(pos, 0.0, 1.0);

    // Store original UV before transformation (for debug visualization)
    output.originalUV = input.texcoord;

    // Apply zoom/pan to UV
    output.texcoord = input.texcoord * uvScale + uvOffset;

    return output;
}

// sRGB to Linear
float3 SRGBToLinear(float3 srgb)
{
    return pow(srgb, 2.2);
}

// Linear to sRGB
float3 LinearToSRGB(float3 linearColor)
{
    return pow(linearColor, 1.0 / 2.2);
}

// Pixel Shader
float4 PSMain(PSInput input) : SV_TARGET
{
    // Clip pixels outside UV [0, 1] range (texture bounds)
    float2 sampledUv = input.texcoord;
    if (enableTiling == 0)
    {
        if (sampledUv.x < 0.0 || sampledUv.x > 1.0 ||
            sampledUv.y < 0.0 || sampledUv.y > 1.0)
        {
            discard;
        }
    }
    else
    {
        sampledUv = frac(sampledUv);
    }

    float4 color;

    // Sample texture with manual mip level or auto mip
    if (mipLevel >= 0.0)
    {
        color = sourceTexture.SampleLevel(texSampler, sampledUv, mipLevel);
    }
    else
    {
        color = sourceTexture.Sample(texSampler, sampledUv);
    }

    // STEP 0: Apply histogram denormalization if enabled
    // CRITICAL: Histogram metadata is in sRGB space, but BC-compressed sRGB textures
    // are auto-decoded to linear by GPU. We must convert back to sRGB, apply denormalization,
    // then convert to linear again for the rest of the pipeline.
    // Formula: v_original = v_normalized * scale + offset
    if (enableHistogramCorrection != 0)
    {
        // For BC*_SRGB_BLOCK formats, GPU auto-decodes to linear before shader
        // Convert linear -> sRGB to match histogram metadata space
        float3 srgbColor = pow(max(color.rgb, 0.0), 1.0 / 2.2);

        // Apply denormalization in sRGB space (use .rgb to ignore w component)
        srgbColor = srgbColor * histogramScale.rgb + histogramOffset.rgb;

        // Convert back to linear for rest of pipeline
        color.rgb = pow(max(srgbColor, 0.0), 2.2);
    }

    // Check if channel mask is active
    bool hasMask = (channelMask != 0xFFFFFFFF);

    // STEP 1: Data is now in LINEAR space
    // For BC-compressed sRGB textures: already converted above
    // For uncompressed PNG textures loaded as R8G8B8A8_UNorm:
    //   - gamma==1.0 (sRGB): decode sRGB->linear
    //   - gamma==2.2 (linear): already linear, no conversion
    if (gamma == 1.0 && enableHistogramCorrection == 0)
    {
        // sRGB texture without histogram - decode to linear
        // (if histogram enabled, we already did this conversion above)
        color.rgb = pow(max(color.rgb, 0.0), 2.2);
    }

    // STEP 2: Apply channel mask in linear space
    // channelMask: bit 0=R, bit 1=G, bit 2=B, bit 3=A, bit 4=grayscale, bit 5=normal reconstruction
    // IMPORTANT: Masks work in LINEAR space for correct values
    if (hasMask)
    {
        bool showR = (channelMask & 0x01) != 0;
        bool showG = (channelMask & 0x02) != 0;
        bool showB = (channelMask & 0x04) != 0;
        bool showA = (channelMask & 0x08) != 0;
        bool showGrayscale = (channelMask & 0x10) != 0;
        bool showNormal = (channelMask & 0x20) != 0;

        if (showNormal)
        {
            // Normal Map Reconstruction Mode (ONLY FOR KTX2 TEXTURES)
            // Extract XY components based on normalLayout metadata from KTX2
            // Supported layouts: 1=RG (UASTC/BC5), 5=RGBxAy (ETC1S)
            float2 nml;

            if (normalLayout == 1)
            {
                // RG layout: X in R, Y in G (BC5/UASTC standard)
                nml.x = color.r;
                nml.y = color.g;
            }
            else if (normalLayout == 5)
            {
                // RGBxAy layout: X in RGB (all channels), Y in A (ETC1S)
                nml.x = color.r; // X encoded in all RGB channels
                nml.y = color.a;
            }
            else
            {
                // NONE (0) or unknown: no reconstruction, show raw texture
                // This happens for PNG sources (R=X, G=Y, B=Z already)
                // Just display color as-is without reconstruction
                color.a = 1.0;
            }

            // Only reconstruct if we have a valid layout
            if (normalLayout == 1 || normalLayout == 5)
            {
                // Unpack from [0,1] to [-1,1]
                nml = nml * 2.0 - 1.0;

                // Reconstruct Z component
                // Z = sqrt(1 - X² - Y²)
                float zSquared = max(0.0, 1.0 - dot(nml, nml));
                float nmlZ = sqrt(zSquared);

                // Pack back to [0,1] for display
                float3 normalVis = float3(nml.x, nml.y, nmlZ);
                normalVis = normalVis * 0.5 + 0.5; // [-1,1] -> [0,1] for visualization

                color.rgb = normalVis;
                color.a = 1.0;
            }
        }
        else if (showGrayscale)
        {
            // Show single channel as grayscale (already in linear)
            float value = 0.0;
            if (showR) value = color.r;
            else if (showG) value = color.g;
            else if (showB) value = color.b;
            else if (showA) value = color.a;
            color.rgb = float3(value, value, value);
        }
        else
        {
            // Apply channel mask (show selected channels, zero others)
            color.r = showR ? color.r : 0.0;
            color.g = showG ? color.g : 0.0;
            color.b = showB ? color.b : 0.0;
            color.a = showA ? color.a : 1.0;
        }
    }

    // STEP 3: Apply HDR exposure in linear space
    if (exposure != 0.0)
    {
        float exposureMul = exp2(exposure);
        color.rgb *= exposureMul;
    }

    // STEP 4: Encode to sRGB for monitor display
    // Monitors expect sRGB-encoded data, so we always need to encode linear->sRGB
    // For sRGB textures: we decoded earlier, now re-encode
    // For linear textures: encode to sRGB for first time
    color.rgb = pow(max(color.rgb, 0.0), 1.0 / 2.2);

    return color;
}
