// Texture Viewer Shader
// Supports: sRGB/Linear toggle, manual mip level, HDR exposure, channel swizzle

cbuffer ShaderConstants : register(b0)
{
    float2 uvScale;      // UV scaling for zoom
    float2 uvOffset;     // UV offset for pan
    float2 posScale;     // Position scaling for aspect ratio correction
    float mipLevel;      // Manual mip level (-1 for auto)
    float exposure;      // HDR exposure (EV)
    float gamma;         // Gamma correction (2.2 for sRGB, 1.0 for linear)
    uint channelMask;    // RGBA channel mask (bit flags)
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

    // Apply aspect ratio correction to position
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
    if (input.texcoord.x < 0.0 || input.texcoord.x > 1.0 ||
        input.texcoord.y < 0.0 || input.texcoord.y > 1.0)
    {
        discard;
    }

    float4 color;

    // Sample texture with manual mip level or auto mip
    if (mipLevel >= 0.0)
    {
        color = sourceTexture.SampleLevel(texSampler, input.texcoord, mipLevel);
    }
    else
    {
        color = sourceTexture.Sample(texSampler, input.texcoord);
    }

    // Check if channel mask is active
    bool hasMask = (channelMask != 0xFFFFFFFF);

    // STEP 1: Convert texture data to LINEAR space (for both masked and unmasked)
    // For sRGB textures (gamma==1.0): decode sRGB->linear
    // For linear textures (gamma==2.2): already linear, no conversion needed
    if (gamma == 1.0)
    {
        // sRGB texture - decode to linear
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
            // Normal Map Reconstruction Mode
            // Standard BC5 format: X in Red/Green channel, Y in Green/Alpha channel, Z reconstructed
            // Load XY from appropriate channels (GA is common for BC5)
            float2 nml;
            nml.x = color.r; // X from Red (or could be Green for BC5)
            nml.y = color.a; // Y from Alpha (or could be Green for BC5)

            // Unpack from [0,1] to [-1,1]
            nml = nml * 2.0 - 1.0;

            // Reconstruct Z component
            // Z = sqrt(1 - dot(XY, XY)) = sqrt(1 - X² - Y²)
            float zSquared = max(0.0, 1.0 - dot(nml, nml));
            float nmlZ = sqrt(zSquared);

            // Pack back to [0,1] for display (so we can see negative values)
            float3 normalVis = float3(nml.x, nml.y, nmlZ);
            normalVis = normalVis * 0.5 + 0.5; // [-1,1] -> [0,1] for visualization

            color.rgb = normalVis;
            color.a = 1.0;
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
