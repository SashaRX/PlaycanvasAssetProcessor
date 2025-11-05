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
};

// Vertex Shader: Full-screen quad
PSInput VSMain(VSInput input)
{
    PSInput output;

    // Apply aspect ratio correction to position
    float2 pos = input.position * posScale;
    output.position = float4(pos, 0.0, 1.0);

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

    // Apply channel mask (for R/G/B/A channel viewing)
    // channelMask: bit 0=R, bit 1=G, bit 2=B, bit 3=A, bit 4=grayscale alpha
    if (channelMask != 0xFFFFFFFF)
    {
        bool showR = (channelMask & 0x01) != 0;
        bool showG = (channelMask & 0x02) != 0;
        bool showB = (channelMask & 0x04) != 0;
        bool showA = (channelMask & 0x08) != 0;
        bool showAlphaGray = (channelMask & 0x10) != 0;

        if (showAlphaGray)
        {
            // Show alpha as grayscale
            float alpha = color.a;
            return float4(alpha, alpha, alpha, 1.0);
        }
        else
        {
            // Apply channel mask
            color.r = showR ? color.r : 0.0;
            color.g = showG ? color.g : 0.0;
            color.b = showB ? color.b : 0.0;
            color.a = showA ? color.a : 1.0;
        }
    }

    // Apply HDR exposure (if enabled, exposure != 0)
    if (exposure != 0.0)
    {
        float exposureMul = exp2(exposure);
        color.rgb *= exposureMul;
    }

    // Apply gamma correction
    // If gamma == 1.0, no correction (linear)
    // If gamma == 2.2, apply sRGB encoding
    if (gamma != 1.0)
    {
        color.rgb = pow(max(color.rgb, 0.0), 1.0 / gamma);
    }

    return color;
}
