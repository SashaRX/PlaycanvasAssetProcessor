namespace AssetProcessor.MasterMaterials.Models;

/// <summary>
/// Default PlayCanvas shader chunks (API version 2.15+)
/// These chunks are read-only and can be copied to create custom versions
/// </summary>
public static class DefaultShaderChunks
{
    public const string ApiVersion = "2.15";

    /// <summary>
    /// Gets all default PlayCanvas shader chunks
    /// </summary>
    public static IReadOnlyList<ShaderChunk> GetAllChunks() => _chunks;

    private static readonly List<ShaderChunk> _chunks =
    [
        // ===== DIFFUSE =====
        new ShaderChunk
        {
            Id = "diffusePS",
            Type = "fragment",
            Description = "Diffuse color/texture sampling",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
#ifdef MAPCOLOR
uniform vec3 material_diffuse;
#endif

#ifdef MAPTEXTURE
uniform sampler2D texture_diffuseMap;
#endif

void getAlbedo() {
    dAlbedo = vec3(1.0);

#ifdef MAPCOLOR
    dAlbedo *= material_diffuse;
#endif

#ifdef MAPTEXTURE
    vec3 albedoBase = gammaCorrectInput(texture2DBias(texture_diffuseMap, $UV, textureBias).$CH);
    dAlbedo *= addAlbedoDetail(albedoBase);
#endif

#ifdef MAPVERTEX
    dAlbedo *= gammaCorrectInput(saturate(vVertexColor.$VC));
#endif
}
",
            Wgsl = @"
uniform material_diffuse: vec3f;

#ifdef STD_DIFFUSEDETAIL_TEXTURE
    #include ""detailModesPS""
#endif

fn getAlbedo() {
    dAlbedo = uniform.material_diffuse.rgb;

    #ifdef STD_DIFFUSE_TEXTURE
        var albedoTexture: vec3f = {STD_DIFFUSE_TEXTURE_DECODE}(textureSampleBias({STD_DIFFUSE_TEXTURE_NAME}, {STD_DIFFUSE_TEXTURE_NAME}Sampler, {STD_DIFFUSE_TEXTURE_UV}, uniform.textureBias)).{STD_DIFFUSE_TEXTURE_CHANNEL};

        #ifdef STD_DIFFUSEDETAIL_TEXTURE
            var albedoDetail: vec3f = {STD_DIFFUSEDETAIL_TEXTURE_DECODE}(textureSampleBias({STD_DIFFUSEDETAIL_TEXTURE_NAME}, {STD_DIFFUSEDETAIL_TEXTURE_NAME}Sampler, {STD_DIFFUSEDETAIL_TEXTURE_UV}, uniform.textureBias)).{STD_DIFFUSEDETAIL_TEXTURE_CHANNEL};
            albedoTexture = detailMode_{STD_DIFFUSEDETAIL_DETAILMODE}(albedoTexture, albedoDetail);
        #endif

        dAlbedo = dAlbedo * albedoTexture;
    #endif

    #ifdef STD_DIFFUSE_VERTEX
        dAlbedo = dAlbedo * saturate3(vVertexColor.{STD_DIFFUSE_VERTEX_CHANNEL});
    #endif
}
"
        },

        // ===== NORMAL =====
        new ShaderChunk
        {
            Id = "normalMapPS",
            Type = "fragment",
            Description = "Normal map sampling and transformation",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
#ifdef MAPTEXTURE
uniform sampler2D texture_normalMap;
uniform float material_bumpiness;
#endif

void getNormal() {
#ifdef MAPTEXTURE
    vec3 normalMap = unpackNormal(texture2DBias(texture_normalMap, $UV, textureBias));
    normalMap = mix(vec3(0.0, 0.0, 1.0), normalMap, material_bumpiness);
    dNormalW = normalize(dTBN * normalMap);
#else
    dNormalW = dVertexNormalW;
#endif
}
",
            Wgsl = @"
#ifdef STD_NORMAL_TEXTURE
    uniform material_bumpiness: f32;
#endif

#ifdef STD_NORMALDETAIL_TEXTURE
    uniform material_normalDetailMapBumpiness: f32;

    fn blendNormals(inN1: vec3f, inN2: vec3f) -> vec3f {
        let n1: vec3f = inN1 + vec3f(0.0, 0.0, 1.0);
        let n2: vec3f = inN2 * vec3f(-1.0, -1.0, 1.0);
        return n1 * dot(n1, n2) / n1.z - n2;
    }
#endif

fn getNormal() {
#ifdef STD_NORMAL_TEXTURE
    var normalMap: vec3f = {STD_NORMAL_TEXTURE_DECODE}(textureSampleBias({STD_NORMAL_TEXTURE_NAME}, {STD_NORMAL_TEXTURE_NAME}Sampler, {STD_NORMAL_TEXTURE_UV}, uniform.textureBias));
    normalMap = mix(vec3f(0.0, 0.0, 1.0), normalMap, uniform.material_bumpiness);

    #ifdef STD_NORMALDETAIL_TEXTURE
        var normalDetailMap: vec3f = {STD_NORMALDETAIL_TEXTURE_DECODE}(textureSampleBias({STD_NORMALDETAIL_TEXTURE_NAME}, {STD_NORMALDETAIL_TEXTURE_NAME}Sampler, {STD_NORMALDETAIL_TEXTURE_UV}, uniform.textureBias));
        normalDetailMap = mix(vec3f(0.0, 0.0, 1.0), normalDetailMap, uniform.material_normalDetailMapBumpiness);
        normalMap = blendNormals(normalMap, normalDetailMap);
    #endif

    dNormalW = normalize(dTBN * normalMap);
#else
    dNormalW = dVertexNormalW;
#endif
}
"
        },

        // ===== SPECULAR =====
        new ShaderChunk
        {
            Id = "specularPS",
            Type = "fragment",
            Description = "Specular color sampling",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
#ifdef MAPCOLOR
uniform vec3 material_specular;
#endif

#ifdef MAPTEXTURE
uniform sampler2D texture_specularMap;
#endif

void getSpecularity() {
    dSpecularity = vec3(1.0);

#ifdef MAPCOLOR
    dSpecularity *= material_specular;
#endif

#ifdef MAPTEXTURE
    dSpecularity *= texture2DBias(texture_specularMap, $UV, textureBias).$CH;
#endif

#ifdef MAPVERTEX
    dSpecularity *= saturate(vVertexColor.$VC);
#endif
}
",
            Wgsl = @"
#ifdef STD_SPECULAR_CONSTANT
    uniform material_specular: vec3f;
#endif

fn getSpecularity() {
    var specularColor = vec3f(1.0, 1.0, 1.0);

    #ifdef STD_SPECULAR_CONSTANT
    specularColor = specularColor * uniform.material_specular;
    #endif

    #ifdef STD_SPECULAR_TEXTURE
    specularColor = specularColor * {STD_SPECULAR_TEXTURE_DECODE}(textureSampleBias({STD_SPECULAR_TEXTURE_NAME}, {STD_SPECULAR_TEXTURE_NAME}Sampler, {STD_SPECULAR_TEXTURE_UV}, uniform.textureBias)).{STD_SPECULAR_TEXTURE_CHANNEL};
    #endif

    #ifdef STD_SPECULAR_VERTEX
    specularColor = specularColor * saturate3(vVertexColor.{STD_SPECULAR_VERTEX_CHANNEL});
    #endif

    dSpecularity = specularColor;
}
"
        },

        // ===== GLOSS =====
        new ShaderChunk
        {
            Id = "glossPS",
            Type = "fragment",
            Description = "Glossiness/roughness sampling",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
#ifdef MAPFLOAT
uniform float material_gloss;
#endif

#ifdef MAPTEXTURE
uniform sampler2D texture_glossMap;
#endif

void getGlossiness() {
    dGlossiness = 1.0;

#ifdef MAPFLOAT
    dGlossiness *= material_gloss;
#endif

#ifdef MAPTEXTURE
    dGlossiness *= texture2DBias(texture_glossMap, $UV, textureBias).$CH;
#endif

#ifdef MAPVERTEX
    dGlossiness *= saturate(vVertexColor.$VC);
#endif

#ifdef MAPINVERT
    dGlossiness = 1.0 - dGlossiness;
#endif
}
",
            Wgsl = @"
#ifdef STD_GLOSS_CONSTANT
    uniform material_gloss: f32;
#endif

fn getGlossiness() {
    dGlossiness = 1.0;

    #ifdef STD_GLOSS_CONSTANT
    dGlossiness = dGlossiness * uniform.material_gloss;
    #endif

    #ifdef STD_GLOSS_TEXTURE
    dGlossiness = dGlossiness * textureSampleBias({STD_GLOSS_TEXTURE_NAME}, {STD_GLOSS_TEXTURE_NAME}Sampler, {STD_GLOSS_TEXTURE_UV}, uniform.textureBias).{STD_GLOSS_TEXTURE_CHANNEL};
    #endif

    #ifdef STD_GLOSS_VERTEX
    dGlossiness = dGlossiness * saturate(vVertexColor.{STD_GLOSS_VERTEX_CHANNEL});
    #endif

    #ifdef STD_GLOSS_INVERT
    dGlossiness = 1.0 - dGlossiness;
    #endif

    dGlossiness = dGlossiness + 0.0000001;
}
"
        },

        // ===== METALNESS =====
        new ShaderChunk
        {
            Id = "metalnessPS",
            Type = "fragment",
            Description = "Metalness value sampling",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
#ifdef MAPFLOAT
uniform float material_metalness;
#endif

#ifdef MAPTEXTURE
uniform sampler2D texture_metalnessMap;
#endif

void getMetalness() {
    dMetalness = 1.0;

#ifdef MAPFLOAT
    dMetalness *= material_metalness;
#endif

#ifdef MAPTEXTURE
    dMetalness *= texture2DBias(texture_metalnessMap, $UV, textureBias).$CH;
#endif

#ifdef MAPVERTEX
    dMetalness *= saturate(vVertexColor.$VC);
#endif
}
",
            Wgsl = @"
#ifdef STD_METALNESS_CONSTANT
uniform material_metalness: f32;
#endif

fn getMetalness() {
    var metalness: f32 = 1.0;

    #ifdef STD_METALNESS_CONSTANT
        metalness = metalness * uniform.material_metalness;
    #endif

    #ifdef STD_METALNESS_TEXTURE
        metalness = metalness * textureSampleBias({STD_METALNESS_TEXTURE_NAME}, {STD_METALNESS_TEXTURE_NAME}Sampler, {STD_METALNESS_TEXTURE_UV}, uniform.textureBias).{STD_METALNESS_TEXTURE_CHANNEL};
    #endif

    #ifdef STD_METALNESS_VERTEX
    metalness = metalness * saturate(vVertexColor.{STD_METALNESS_VERTEX_CHANNEL});
    #endif

    dMetalness = metalness;
}
"
        },

        // ===== AMBIENT OCCLUSION =====
        new ShaderChunk
        {
            Id = "aoPS",
            Type = "fragment",
            Description = "Ambient occlusion sampling",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
#ifdef MAPTEXTURE
uniform sampler2D texture_aoMap;
#endif

void getAO() {
    dAo = 1.0;

#ifdef MAPTEXTURE
    dAo *= texture2DBias(texture_aoMap, $UV, textureBias).$CH;
#endif

#ifdef MAPVERTEX
    dAo *= saturate(vVertexColor.$VC);
#endif
}
",
            Wgsl = @"
#if defined(STD_AO_TEXTURE) || defined(STD_AO_VERTEX)
    uniform material_aoIntensity: f32;
#endif

#ifdef STD_AODETAIL_TEXTURE
    #include ""detailModesPS""
#endif

fn getAO() {
    dAo = 1.0;

    #ifdef STD_AO_TEXTURE
        var aoBase: f32 = textureSampleBias({STD_AO_TEXTURE_NAME}, {STD_AO_TEXTURE_NAME}Sampler, {STD_AO_TEXTURE_UV}, uniform.textureBias).{STD_AO_TEXTURE_CHANNEL};

        #ifdef STD_AODETAIL_TEXTURE
            var aoDetail: f32 = textureSampleBias({STD_AODETAIL_TEXTURE_NAME}, {STD_AODETAIL_TEXTURE_NAME}Sampler, {STD_AODETAIL_TEXTURE_UV}, uniform.textureBias).{STD_AODETAIL_TEXTURE_CHANNEL};
            aoBase = detailMode_{STD_AODETAIL_DETAILMODE}(vec3f(aoBase), vec3f(aoDetail)).r;
        #endif

        dAo = dAo * aoBase;
    #endif

    #ifdef STD_AO_VERTEX
        dAo = dAo * saturate(vVertexColor.{STD_AO_VERTEX_CHANNEL});
    #endif

    #if defined(STD_AO_TEXTURE) || defined(STD_AO_VERTEX)
        dAo = mix(1.0, dAo, uniform.material_aoIntensity);
    #endif
}
"
        },

        // ===== EMISSIVE =====
        new ShaderChunk
        {
            Id = "emissivePS",
            Type = "fragment",
            Description = "Emissive color/texture sampling",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
#ifdef MAPCOLOR
uniform vec3 material_emissive;
#endif

#ifdef MAPFLOAT
uniform float material_emissiveIntensity;
#endif

#ifdef MAPTEXTURE
uniform sampler2D texture_emissiveMap;
#endif

void getEmission() {
    dEmission = vec3(0.0);

#ifdef MAPCOLOR
    dEmission = material_emissive;
#endif

#ifdef MAPFLOAT
    dEmission *= material_emissiveIntensity;
#endif

#ifdef MAPTEXTURE
    dEmission *= gammaCorrectInput(texture2DBias(texture_emissiveMap, $UV, textureBias).$CH);
#endif

#ifdef MAPVERTEX
    dEmission *= gammaCorrectInput(saturate(vVertexColor.$VC));
#endif
}
",
            Wgsl = @"
uniform material_emissive: vec3f;
uniform material_emissiveIntensity: f32;

fn getEmission() {
    dEmission = uniform.material_emissive * uniform.material_emissiveIntensity;

    #ifdef STD_EMISSIVE_TEXTURE
    dEmission *= {STD_EMISSIVE_TEXTURE_DECODE}(textureSampleBias({STD_EMISSIVE_TEXTURE_NAME}, {STD_EMISSIVE_TEXTURE_NAME}Sampler, {STD_EMISSIVE_TEXTURE_UV}, uniform.textureBias)).{STD_EMISSIVE_TEXTURE_CHANNEL};
    #endif

    #ifdef STD_EMISSIVE_VERTEX
    dEmission = dEmission * saturate3(vVertexColor.{STD_EMISSIVE_VERTEX_CHANNEL});
    #endif
}
"
        },

        // ===== OPACITY =====
        new ShaderChunk
        {
            Id = "opacityPS",
            Type = "fragment",
            Description = "Opacity/alpha sampling",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
#ifdef MAPFLOAT
uniform float material_opacity;
#endif

#ifdef MAPTEXTURE
uniform sampler2D texture_opacityMap;
#endif

void getOpacity() {
    dAlpha = 1.0;

#ifdef MAPFLOAT
    dAlpha *= material_opacity;
#endif

#ifdef MAPTEXTURE
    dAlpha *= texture2DBias(texture_opacityMap, $UV, textureBias).$CH;
#endif

#ifdef MAPVERTEX
    dAlpha *= saturate(vVertexColor.$VC);
#endif
}
",
            Wgsl = @"
uniform material_opacity: f32;

fn getOpacity() {
    dAlpha = uniform.material_opacity;

    #ifdef STD_OPACITY_TEXTURE
    dAlpha = dAlpha * textureSampleBias({STD_OPACITY_TEXTURE_NAME}, {STD_OPACITY_TEXTURE_NAME}Sampler, {STD_OPACITY_TEXTURE_UV}, uniform.textureBias).{STD_OPACITY_TEXTURE_CHANNEL};
    #endif

    #ifdef STD_OPACITY_VERTEX
    dAlpha = dAlpha * clamp(vVertexColor.{STD_OPACITY_VERTEX_CHANNEL}, 0.0, 1.0);
    #endif
}
"
        },

        // ===== ALPHA TEST =====
        new ShaderChunk
        {
            Id = "alphaTestPS",
            Type = "fragment",
            Description = "Alpha test (cutout) functionality",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
uniform float alpha_ref;

void alphaTest(float a) {
    if (a < alpha_ref) discard;
}
",
            Wgsl = @"
uniform alpha_ref: f32;

fn alphaTest(a: f32) {
    if (a < uniform.alpha_ref) {
        discard;
    }
}
"
        },

        // ===== CLEARCOAT =====
        new ShaderChunk
        {
            Id = "clearCoatPS",
            Type = "fragment",
            Description = "Clear coat layer",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
#ifdef MAPFLOAT
uniform float material_clearCoat;
#endif

#ifdef MAPTEXTURE
uniform sampler2D texture_clearCoatMap;
#endif

void getClearCoat() {
    ccSpecularity = 1.0;

#ifdef MAPFLOAT
    ccSpecularity *= material_clearCoat;
#endif

#ifdef MAPTEXTURE
    ccSpecularity *= texture2DBias(texture_clearCoatMap, $UV, textureBias).$CH;
#endif
}
"
        },

        // ===== CLEARCOAT GLOSS =====
        new ShaderChunk
        {
            Id = "clearCoatGlossPS",
            Type = "fragment",
            Description = "Clear coat glossiness",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
#ifdef MAPFLOAT
uniform float material_clearCoatGloss;
#endif

#ifdef MAPTEXTURE
uniform sampler2D texture_clearCoatGlossMap;
#endif

void getClearCoatGlossiness() {
    ccGlossiness = 1.0;

#ifdef MAPFLOAT
    ccGlossiness *= material_clearCoatGloss;
#endif

#ifdef MAPTEXTURE
    ccGlossiness *= texture2DBias(texture_clearCoatGlossMap, $UV, textureBias).$CH;
#endif

#ifdef MAPINVERT
    ccGlossiness = 1.0 - ccGlossiness;
#endif
}
"
        },

        // ===== CLEARCOAT NORMAL =====
        new ShaderChunk
        {
            Id = "clearCoatNormalPS",
            Type = "fragment",
            Description = "Clear coat normal map",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
#ifdef MAPTEXTURE
uniform sampler2D texture_clearCoatNormalMap;
uniform float material_clearCoatBumpiness;
#endif

void getClearCoatNormal() {
#ifdef MAPTEXTURE
    vec3 normalMap = unpackNormal(texture2DBias(texture_clearCoatNormalMap, $UV, textureBias));
    normalMap = mix(vec3(0.0, 0.0, 1.0), normalMap, material_clearCoatBumpiness);
    ccNormalW = normalize(dTBN * normalMap);
#else
    ccNormalW = dVertexNormalW;
#endif
}
"
        },

        // ===== LIGHTMAP =====
        new ShaderChunk
        {
            Id = "lightmapSinglePS",
            Type = "fragment",
            Description = "Single lightmap sampling",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
uniform sampler2D texture_lightMap;

void getLightMap() {
    dLightmap = gammaCorrectInput(texture2D(texture_lightMap, $UV).$CH);
}
"
        },

        // ===== FRESNEL =====
        new ShaderChunk
        {
            Id = "fresnelSchlickPS",
            Type = "fragment",
            Description = "Schlick Fresnel approximation",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
vec3 getFresnel(float cosTheta, vec3 f0) {
    float fresnel = pow(1.0 - max(cosTheta, 0.0), 5.0);
    return f0 + (vec3(1.0) - f0) * fresnel;
}
",
            Wgsl = @"
// Schlick's approximation
fn getFresnel(
        cosTheta: f32,
        gloss: f32,
        specularity: vec3f
    #if defined(LIT_IRIDESCENCE)
        , iridescenceFresnel: vec3f,
        iridescenceIntensity: f32
    #endif
) -> vec3f {
    let fresnel: f32 = pow(1.0 - saturate(cosTheta), 5.0);
    let glossSq: f32 = gloss * gloss;
    let specIntensity: f32 = max(specularity.r, max(specularity.g, specularity.b));
    let ret: vec3f = specularity + (max(vec3f(glossSq * specIntensity), specularity) - specularity) * fresnel;

    #if defined(LIT_IRIDESCENCE)
        return mix(ret, iridescenceFresnel, iridescenceIntensity);
    #else
        return ret;
    #endif
}

fn getFresnelCC(cosTheta: f32) -> f32 {
    let fresnel: f32 = pow(1.0 - saturate(cosTheta), 5.0);
    return 0.04 + (1.0 - 0.04) * fresnel;
}
"
        },

        // ===== TONEMAPPING =====
        new ShaderChunk
        {
            Id = "tonemappingAcesPS",
            Type = "fragment",
            Description = "ACES tonemapping",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
uniform float exposure;

vec3 toneMap(vec3 color) {
    float tA = 2.51;
    float tB = 0.03;
    float tC = 2.43;
    float tD = 0.59;
    float tE = 0.14;
    vec3 x = color * exposure;
    return (x * (tA * x + tB)) / (x * (tC * x + tD) + tE);
}
",
            Wgsl = @"
uniform exposure: f32;

fn toneMap(color: vec3f) -> vec3f {
    let tA: f32 = 2.51;
    let tB: f32 = 0.03;
    let tC: f32 = 2.43;
    let tD: f32 = 0.59;
    let tE: f32 = 0.14;
    let x: vec3f = color * uniform.exposure;
    return (x * (tA * x + tB)) / (x * (tC * x + tD) + tE);
}
"
        },

        // ===== FOG =====
        new ShaderChunk
        {
            Id = "fogPS",
            Type = "fragment",
            Description = "Fog calculations (linear/exp/exp2)",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
uniform vec3 fog_color;
uniform float fog_density;
uniform float fog_start;
uniform float fog_end;

vec3 addFog(vec3 color) {
    float depth = gl_FragCoord.z / gl_FragCoord.w;

#ifdef FOG_LINEAR
    float fogFactor = (fog_end - depth) / (fog_end - fog_start);
#endif

#ifdef FOG_EXP
    float fogFactor = exp(-fog_density * depth);
#endif

#ifdef FOG_EXP2
    float fogFactor = exp(-fog_density * fog_density * depth * depth);
#endif

    fogFactor = clamp(fogFactor, 0.0, 1.0);
    return mix(fog_color, color, fogFactor);
}
"
        },

        // ===== GAMMA =====
        new ShaderChunk
        {
            Id = "gammaPS",
            Type = "fragment",
            Description = "Gamma correction functions",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
float gammaCorrectInput(float color) {
    return pow(color, 2.2);
}

vec3 gammaCorrectInput(vec3 color) {
    return pow(color, vec3(2.2));
}

vec4 gammaCorrectInput(vec4 color) {
    return vec4(pow(color.rgb, vec3(2.2)), color.a);
}

vec3 gammaCorrectOutput(vec3 color) {
#ifdef HDR
    return color;
#else
    return pow(color + 0.0000001, vec3(1.0 / 2.2));
#endif
}
",
            Wgsl = @"
#include ""decodePS""

#if (GAMMA == SRGB)
    fn gammaCorrectInput(color: f32) -> f32 {
        return decodeGammaFloat(color);
    }

    fn gammaCorrectInputVec3(color: vec3f) -> vec3f {
        return decodeGamma3(color);
    }

    fn gammaCorrectInputVec4(color: vec4f) -> vec4f {
        return vec4f(decodeGamma3(color.xyz), color.w);
    }

    fn gammaCorrectOutput(color: vec3f) -> vec3f {
        return pow(color + 0.0000001, vec3f(1.0 / 2.2));
    }
#else
    fn gammaCorrectInput(color: f32) -> f32 {
        return color;
    }

    fn gammaCorrectInputVec3(color: vec3f) -> vec3f {
        return color;
    }

    fn gammaCorrectInputVec4(color: vec4f) -> vec4f {
        return color;
    }

    fn gammaCorrectOutput(color: vec3f) -> vec3f {
        return color;
    }
#endif
"
        },

        // ===== TRANSFORM (Vertex) =====
        new ShaderChunk
        {
            Id = "transformVS",
            Type = "vertex",
            Description = "Vertex position transformation",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
mat4 getModelMatrix() {
#ifdef DYNAMICBATCH
    return getBoneMatrix(vertex_boneIndices);
#elif defined(SKIN)
    return getSkinMatrix(vertex_boneIndices, vertex_boneWeights);
#elif defined(INSTANCING)
    return mat4(instance_line1, instance_line2, instance_line3, instance_line4);
#else
    return matrix_model;
#endif
}

vec4 getPosition() {
    dModelMatrix = getModelMatrix();
    vec3 localPos = vertex_position;

#ifdef MORPHING_POSITION
    localPos += getPositionMorph();
#endif

    vec4 posW = dModelMatrix * vec4(localPos, 1.0);
    dPositionW = posW.xyz;

    return matrix_viewProjection * posW;
}
"
        },

        // ===== NORMAL (Vertex) =====
        new ShaderChunk
        {
            Id = "normalVS",
            Type = "vertex",
            Description = "Vertex normal transformation",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
vec3 getNormal() {
    dNormalMatrix = getNormalMatrix(dModelMatrix);
    vec3 localNormal = vertex_normal;

#ifdef MORPHING_NORMAL
    localNormal += getNormalMorph();
#endif

    return normalize(dNormalMatrix * localNormal);
}
"
        },

        // ===== TBN =====
        new ShaderChunk
        {
            Id = "TBNPS",
            Type = "fragment",
            Description = "Tangent-Bitangent-Normal matrix",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
void getTBN(vec3 tangent, vec3 binormal, vec3 normal) {
#ifdef MAPTEXTURE
    dTBN = mat3(normalize(tangent), normalize(binormal), normalize(normal));
#endif
}

void getTBN() {
#ifdef MAPTEXTURE
    vec3 B = cross(dVertexNormalW, vTangentW.xyz) * vTangentW.w;
    dTBN = mat3(normalize(vTangentW.xyz), normalize(B), normalize(dVertexNormalW));
#endif
}
"
        },

        // ===== REFLECTIONS =====
        new ShaderChunk
        {
            Id = "reflectionPS",
            Type = "fragment",
            Description = "Environment reflections",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
uniform samplerCube texture_cubeMap;
uniform float material_reflectivity;

void addReflection(vec3 worldNormal, float gloss) {
    vec3 reflectDir = reflect(-dViewDirW, worldNormal);
    float lod = (1.0 - gloss) * 5.0;
    vec3 reflection = textureCubeLodEXT(texture_cubeMap, fixSeamsStatic(reflectDir, lod), lod).rgb;
    reflection = processEnvironment(reflection);
    dReflection += reflection * material_reflectivity;
}
"
        },

        // ===== START (Fragment) =====
        new ShaderChunk
        {
            Id = "startPS",
            Type = "fragment",
            Description = "Fragment shader initialization",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
void main(void) {
    dDiffuseLight = vec3(0);
    dSpecularLight = vec3(0);
    dReflection = vec3(0);
    dSpecularity = vec3(0);
    dAlbedo = vec3(0);
    dEmission = vec3(0);
    dNormalW = vec3(0, 0, 1);
    dVertexNormalW = vNormalW;
    dGlossiness = 0.0;
    dAlpha = 1.0;
    dAo = 1.0;
    dMetalness = 0.0;
    dLightmap = vec3(0);
    dViewDirW = normalize(view_position - vPositionW);
"
        },

        // ===== END (Fragment) =====
        new ShaderChunk
        {
            Id = "endPS",
            Type = "fragment",
            Description = "Fragment shader finalization",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
    vec3 finalColor = combineColor();

#ifdef FOG
    finalColor = addFog(finalColor);
#endif

    finalColor = toneMap(finalColor);
    finalColor = gammaCorrectOutput(finalColor);

    gl_FragColor = vec4(finalColor, dAlpha);
}
"
        },

        // ===== COMBINE =====
        new ShaderChunk
        {
            Id = "combinePS",
            Type = "fragment",
            Description = "Combine diffuse and specular lighting",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
vec3 combineColor() {
    vec3 albedo = dAlbedo;
    vec3 specularity = dSpecularity;

#ifdef LIT
    #ifdef METALNESS
        vec3 f0 = mix(vec3(0.04), albedo, dMetalness);
        specularity = f0;
        albedo *= (1.0 - dMetalness);
    #endif

    vec3 diffuse = albedo * (dDiffuseLight + dLightmap);
    vec3 specular = specularity * dSpecularLight;

    #ifdef REFLECTION
        specular += dReflection * specularity;
    #endif

    vec3 result = diffuse + specular;

    #ifdef AMBIENT
        result += albedo * dAo * dAmbientLight;
    #endif
#else
    vec3 result = albedo;
#endif

    result += dEmission;
    return result;
}
",
            Wgsl = @"
fn combineColor(albedo: vec3f, sheenSpecularity: vec3f, clearcoatSpecularity: f32) -> vec3f {
    var ret: vec3f = vec3f(0.0);

    #ifdef LIT_OLD_AMBIENT
        ret = ret + ((dDiffuseLight - uniform.light_globalAmbient) * albedo + uniform.material_ambient * uniform.light_globalAmbient);
    #else
        ret = ret + (albedo * dDiffuseLight);
    #endif

    #ifdef LIT_SPECULAR
        ret = ret + dSpecularLight;
    #endif

    #ifdef LIT_REFLECTIONS
        ret = ret + (dReflection.rgb * dReflection.a);
    #endif

    #ifdef LIT_SHEEN
        let sheenScaling: f32 = 1.0 - max(max(sheenSpecularity.r, sheenSpecularity.g), sheenSpecularity.b) * 0.157;
        ret = ret * sheenScaling + (sSpecularLight + sReflection.rgb) * sheenSpecularity;
    #endif

    #ifdef LIT_CLEARCOAT
        let clearCoatScaling: f32 = 1.0 - ccFresnel * clearcoatSpecularity;
        ret = ret * clearCoatScaling + (ccSpecularLight + ccReflection) * clearcoatSpecularity;
    #endif

    return ret;
}
"
        },

        // ===== PARALLAX =====
        new ShaderChunk
        {
            Id = "parallaxPS",
            Type = "fragment",
            Description = "Parallax occlusion mapping",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
uniform sampler2D texture_heightMap;
uniform float material_heightMapFactor;

vec2 getParallax() {
    float h = texture2D(texture_heightMap, $UV).r;
    vec3 viewDirT = normalize(vViewDirT);
    h = h * material_heightMapFactor - material_heightMapFactor * 0.5;
    return $UV + h * viewDirT.xy;
}
"
        },

        // ===== REFRACTION =====
        new ShaderChunk
        {
            Id = "refractionPS",
            Type = "fragment",
            Description = "Refraction for transparent materials",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
uniform float material_refraction;
uniform float material_refractionIndex;
uniform sampler2D uSceneColorMap;
uniform vec4 uScreenSize;

void addRefraction() {
    vec3 refractionDir = refract(-dViewDirW, dNormalW, material_refractionIndex);
    vec2 refractionCoord = gl_FragCoord.xy * uScreenSize.zw;
    refractionCoord += refractionDir.xy * material_refraction;
    vec3 refraction = texture2D(uSceneColorMap, refractionCoord).rgb;
    dAlbedo = mix(dAlbedo, refraction, material_refraction);
}
"
        },

        // ===== SPHERICAL COORDINATES =====
        new ShaderChunk
        {
            Id = "sphericalPS",
            Type = "fragment",
            Description = "Equirectangular/spherical coordinate helper functions",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
// equirectangular helper functions
vec2 toSpherical(vec3 dir) {
    return vec2(dir.xz == vec2(0.0) ? 0.0 : atan(dir.x, dir.z), asin(dir.y));
}

vec2 toSphericalUv(vec3 dir) {
    const float PI = 3.141592653589793;
    vec2 uv = toSpherical(dir) / vec2(PI * 2.0, PI) + 0.5;
    return vec2(uv.x, 1.0 - uv.y);
}
",
            Wgsl = @"
fn toSpherical(dir: vec3f) -> vec2f {
    let angle_xz = select(0.0, atan2(dir.x, dir.z), any(dir.xz != vec2f(0.0)));
    return vec2f(angle_xz, asin(dir.y));
}

fn toSphericalUv(dir: vec3f) -> vec2f {
    const PI: f32 = 3.141592653589793;
    let uv: vec2f = toSpherical(dir) / vec2f(PI * 2.0, PI) + vec2f(0.5, 0.5);
    return vec2f(uv.x, 1.0 - uv.y);
}
"
        },

        // ===== DECODE =====
        new ShaderChunk
        {
            Id = "decodePS",
            Type = "fragment",
            Description = "Texture decoding functions (Linear, Gamma, RGBM, RGBP, RGBE)",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
#ifndef _DECODE_INCLUDED_
#define _DECODE_INCLUDED_

vec3 decodeLinear(vec4 raw) {
    return raw.rgb;
}

float decodeGamma(float raw) {
    return pow(raw, 2.2);
}

vec3 decodeGamma(vec3 raw) {
    return pow(raw, vec3(2.2));
}

vec3 decodeGamma(vec4 raw) {
    return pow(raw.xyz, vec3(2.2));
}

vec3 decodeRGBM(vec4 raw) {
    vec3 color = (8.0 * raw.a) * raw.rgb;
    return color * color;
}

vec3 decodeRGBP(vec4 raw) {
    vec3 color = raw.rgb * (-raw.a * 7.0 + 8.0);
    return color * color;
}

vec3 decodeRGBE(vec4 raw) {
    if (raw.a == 0.0) {
        return vec3(0.0, 0.0, 0.0);
    } else {
        return raw.xyz * pow(2.0, raw.w * 255.0 - 128.0);
    }
}

vec4 passThrough(vec4 raw) {
    return raw;
}

vec3 unpackNormalXYZ(vec4 nmap) {
    return nmap.xyz * 2.0 - 1.0;
}

vec3 unpackNormalXY(vec4 nmap) {
    vec3 normal;
    normal.xy = nmap.wy * 2.0 - 1.0;
    normal.z = sqrt(1.0 - clamp(dot(normal.xy, normal.xy), 0.0, 1.0));
    return normal;
}

#endif
",
            Wgsl = @"
#ifndef _DECODE_INCLUDED_
#define _DECODE_INCLUDED_

fn decodeLinear(raw: vec4f) -> vec3f {
    return raw.rgb;
}

fn decodeGammaFloat(raw: f32) -> f32 {
    return pow(raw, 2.2);
}

fn decodeGamma3(raw: vec3f) -> vec3f {
    return pow(raw, vec3f(2.2));
}

fn decodeGamma(raw: vec4f) -> vec3f {
    return pow(raw.xyz, vec3f(2.2));
}

fn decodeRGBM(raw: vec4f) -> vec3f {
    let color = (8.0 * raw.a) * raw.rgb;
    return color * color;
}

fn decodeRGBP(raw: vec4f) -> vec3f {
    let color = raw.rgb * (-raw.a * 7.0 + 8.0);
    return color * color;
}

fn decodeRGBE(raw: vec4f) -> vec3f {
    return select(vec3f(0.0), raw.xyz * pow(2.0, raw.w * 255.0 - 128.0), raw.a != 0.0);
}

fn passThrough(raw: vec4f) -> vec4f {
    return raw;
}

fn unpackNormalXYZ(nmap: vec4f) -> vec3f {
    return nmap.xyz * 2.0 - 1.0;
}

fn unpackNormalXY(nmap: vec4f) -> vec3f {
    var xy = nmap.wy * 2.0 - 1.0;
    return vec3f(xy, sqrt(1.0 - clamp(dot(xy, xy), 0.0, 1.0)));
}

#endif
"
        },

        // ===== ENVIRONMENT ATLAS =====
        new ShaderChunk
        {
            Id = "envAtlasPS",
            Type = "fragment",
            Description = "Environment atlas UV mapping functions",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
#ifndef _ENVATLAS_INCLUDED_
#define _ENVATLAS_INCLUDED_

// the envAtlas is fixed at 512 pixels. every equirect is generated with 1 pixel boundary.
const float atlasSize = 512.0;
const float seamSize = 1.0 / atlasSize;

// map a normalized equirect UV to the given rectangle (taking 1 pixel seam into account).
vec2 mapUv(vec2 uv, vec4 rect) {
    return vec2(mix(rect.x + seamSize, rect.x + rect.z - seamSize, uv.x),
                mix(rect.y + seamSize, rect.y + rect.w - seamSize, uv.y));
}

// map a normalized equirect UV and roughness level to the correct atlas rect.
vec2 mapRoughnessUv(vec2 uv, float level) {
    float t = 1.0 / exp2(level);
    return mapUv(uv, vec4(0, 1.0 - t, t, t * 0.5));
}

// map shiny level UV
vec2 mapShinyUv(vec2 uv, float level) {
    float t = 1.0 / exp2(level);
    return mapUv(uv, vec4(1.0 - t, 1.0 - t, t, t * 0.5));
}

#endif
"
        },

        // ===== CUBEMAP ROTATE =====
        new ShaderChunk
        {
            Id = "cubeMapRotatePS",
            Type = "fragment",
            Description = "Cubemap rotation transformation",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
#ifdef CUBEMAP_ROTATION
uniform mat3 cubeMapRotationMatrix;
#endif

vec3 cubeMapRotate(vec3 refDir) {
#ifdef CUBEMAP_ROTATION
    return refDir * cubeMapRotationMatrix;
#else
    return refDir;
#endif
}
"
        },

        // ===== CUBEMAP PROJECT =====
        new ShaderChunk
        {
            Id = "cubeMapProjectPS",
            Type = "fragment",
            Description = "Cubemap projection (none or box)",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
#if LIT_CUBEMAP_PROJECTION == BOX
    uniform vec3 envBoxMin;
    uniform vec3 envBoxMax;
#endif

vec3 cubeMapProject(vec3 nrdir) {

    #if LIT_CUBEMAP_PROJECTION == NONE
        return cubeMapRotate(nrdir);
    #endif

    #if LIT_CUBEMAP_PROJECTION == BOX

        nrdir = cubeMapRotate(nrdir);

        vec3 rbmax = (envBoxMax - vPositionW) / nrdir;
        vec3 rbmin = (envBoxMin - vPositionW) / nrdir;

        vec3 rbminmax = mix(rbmin, rbmax, vec3(greaterThan(nrdir, vec3(0.0))));
        float fa = min(min(rbminmax.x, rbminmax.y), rbminmax.z);

        vec3 posonbox = vPositionW + nrdir * fa;
        vec3 envBoxPos = (envBoxMin + envBoxMax) * 0.5;
        return normalize(posonbox - envBoxPos);

    #endif
}
"
        },

        // ===== ENVIRONMENT PROCESSING =====
        new ShaderChunk
        {
            Id = "envProcPS",
            Type = "fragment",
            Description = "Environment color processing (skybox intensity)",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
#ifdef LIT_SKYBOX_INTENSITY
    uniform float skyboxIntensity;
#endif

vec3 processEnvironment(vec3 color) {
    #ifdef LIT_SKYBOX_INTENSITY
        return color * skyboxIntensity;
    #else
        return color;
    #endif
}
"
        },

        // ===== REFLECTION CUBE =====
        new ShaderChunk
        {
            Id = "reflectionCubePS",
            Type = "fragment",
            Description = "Simple cubemap reflection",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
uniform samplerCube texture_cubeMap;
uniform float material_reflectivity;

vec3 calcReflection(vec3 reflDir, float gloss) {
    vec3 lookupVec = cubeMapProject(reflDir);
    lookupVec.x *= -1.0;
    return {reflectionDecode}(textureCube(texture_cubeMap, lookupVec));
}

void addReflection(vec3 reflDir, float gloss) {
    dReflection += vec4(calcReflection(reflDir, gloss), material_reflectivity);
}
"
        },

        // ===== REFLECTION SPHERE =====
        new ShaderChunk
        {
            Id = "reflectionSpherePS",
            Type = "fragment",
            Description = "Sphere map reflection",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
#ifndef VIEWMATRIX
    #define VIEWMATRIX
    uniform mat4 matrix_view;
#endif
uniform sampler2D texture_sphereMap;
uniform float material_reflectivity;

vec3 calcReflection(vec3 reflDir, float gloss) {
    vec3 reflDirV = (mat3(matrix_view) * reflDir);

    float m = 2.0 * sqrt(dot(reflDirV.xy, reflDirV.xy) + (reflDirV.z + 1.0) * (reflDirV.z + 1.0));
    vec2 sphereMapUv = reflDirV.xy / m + 0.5;

    return {reflectionDecode}(texture2D(texture_sphereMap, sphereMapUv));
}

void addReflection(vec3 reflDir, float gloss) {
    dReflection += vec4(calcReflection(reflDir, gloss), material_reflectivity);
}
"
        },

        // ===== REFLECTION ENV (Standard) =====
        new ShaderChunk
        {
            Id = "reflectionEnvPS",
            Type = "fragment",
            Description = "Environment atlas reflection (standard quality)",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
#ifndef ENV_ATLAS
#define ENV_ATLAS
    uniform sampler2D texture_envAtlas;
#endif
uniform float material_reflectivity;

// calculate mip level for shiny reflection given equirect coords uv.
float shinyMipLevel(vec2 uv) {
    vec2 dx = dFdx(uv);
    vec2 dy = dFdy(uv);

    // calculate second dF at 180 degrees
    vec2 uv2 = vec2(fract(uv.x + 0.5), uv.y);
    vec2 dx2 = dFdx(uv2);
    vec2 dy2 = dFdy(uv2);

    // calculate min of both sets of dF to handle discontinuity at the azim edge
    float maxd = min(max(dot(dx, dx), dot(dy, dy)), max(dot(dx2, dx2), dot(dy2, dy2)));

    return clamp(0.5 * log2(maxd) - 1.0 + textureBias, 0.0, 5.0);
}

vec3 calcReflection(vec3 reflDir, float gloss) {
    vec3 dir = cubeMapProject(reflDir) * vec3(-1.0, 1.0, 1.0);
    vec2 uv = toSphericalUv(dir);

    // calculate roughness level
    float level = saturate(1.0 - gloss) * 5.0;
    float ilevel = floor(level);

    // accessing the shiny (top level) reflection - perform manual mipmap lookup
    float level2 = shinyMipLevel(uv * atlasSize);
    float ilevel2 = floor(level2);

    vec2 uv0, uv1;
    float weight;
    if (ilevel == 0.0) {
        uv0 = mapShinyUv(uv, ilevel2);
        uv1 = mapShinyUv(uv, ilevel2 + 1.0);
        weight = level2 - ilevel2;
    } else {
        // accessing rough reflection - just sample the same part twice
        uv0 = uv1 = mapRoughnessUv(uv, ilevel);
        weight = 0.0;
    }

    vec3 linearA = {reflectionDecode}(texture2D(texture_envAtlas, uv0));
    vec3 linearB = {reflectionDecode}(texture2D(texture_envAtlas, uv1));
    vec3 linear0 = mix(linearA, linearB, weight);
    vec3 linear1 = {reflectionDecode}(texture2D(texture_envAtlas, mapRoughnessUv(uv, ilevel + 1.0)));

    return processEnvironment(mix(linear0, linear1, level - ilevel));
}

void addReflection(vec3 reflDir, float gloss) {
    dReflection += vec4(calcReflection(reflDir, gloss), material_reflectivity);
}
"
        },

        // ===== REFLECTION ENV HQ (High Quality) =====
        new ShaderChunk
        {
            Id = "reflectionEnvHQPS",
            Type = "fragment",
            Description = "Environment atlas + cubemap reflection (high quality)",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
#ifndef ENV_ATLAS
    #define ENV_ATLAS
    uniform sampler2D texture_envAtlas;
#endif
uniform samplerCube texture_cubeMap;
uniform float material_reflectivity;

vec3 calcReflection(vec3 reflDir, float gloss) {
    vec3 dir = cubeMapProject(reflDir) * vec3(-1.0, 1.0, 1.0);
    vec2 uv = toSphericalUv(dir);

    // calculate roughness level
    float level = saturate(1.0 - gloss) * 5.0;
    float ilevel = floor(level);
    float flevel = level - ilevel;

    vec3 sharp = {reflectionCubemapDecode}(textureCube(texture_cubeMap, dir));
    vec3 roughA = {reflectionDecode}(texture2D(texture_envAtlas, mapRoughnessUv(uv, ilevel)));
    vec3 roughB = {reflectionDecode}(texture2D(texture_envAtlas, mapRoughnessUv(uv, ilevel + 1.0)));

    return processEnvironment(mix(sharp, mix(roughA, roughB, flevel), min(level, 1.0)));
}

void addReflection(vec3 reflDir, float gloss) {
    dReflection += vec4(calcReflection(reflDir, gloss), material_reflectivity);
}
"
        },

        // ===== VIEW DIRECTION =====
        new ShaderChunk
        {
            Id = "viewDirPS",
            Type = "fragment",
            Description = "Calculate view direction from camera to fragment",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
void getViewDir() {
    dViewDirW = normalize(view_position - vPositionW);
}
",
            Wgsl = @"
fn getViewDir() {
    dViewDirW = normalize(uniform.view_position - vPositionW);
}
"
        },

        // ===== REFLECTION DIRECTION =====
        new ShaderChunk
        {
            Id = "reflDirPS",
            Type = "fragment",
            Description = "Calculate reflection direction",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
void getReflDir(vec3 worldNormal, vec3 viewDir, float gloss, mat3 tbn) {
    dReflDirW = normalize(-reflect(viewDir, worldNormal));
}
",
            Wgsl = @"
fn getReflDir(worldNormal: vec3f, viewDir: vec3f, gloss: f32, tbn: mat3x3f) {
    dReflDirW = normalize(-reflect(viewDir, worldNormal));
}
"
        },

        // ===== LIGHT DIFFUSE LAMBERT =====
        new ShaderChunk
        {
            Id = "lightDiffuseLambertPS",
            Type = "fragment",
            Description = "Lambertian diffuse lighting",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
float getLightDiffuse(vec3 worldNormal, vec3 viewDir, vec3 lightDirNorm) {
    return max(dot(worldNormal, -lightDirNorm), 0.0);
}
",
            Wgsl = @"
fn getLightDiffuse(worldNormal: vec3f, viewDir: vec3f, lightDirNorm: vec3f) -> f32 {
    return max(dot(worldNormal, -lightDirNorm), 0.0);
}
"
        },

        // ===== LIGHT SPECULAR BLINN =====
        new ShaderChunk
        {
            Id = "lightSpecularBlinnPS",
            Type = "fragment",
            Description = "Blinn-Phong specular lighting",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
// Energy-conserving Blinn-Phong
float calcLightSpecular(float gloss, vec3 worldNormal, vec3 h) {
    float nh = max(dot(h, worldNormal), 0.0);
    float specPow = exp2(gloss * 11.0); // glossiness is linear, power is not; 0 - 2048
    specPow = max(specPow, 0.0001);
    return pow(nh, specPow) * (specPow + 2.0) / 8.0;
}

float getLightSpecular(vec3 h, vec3 reflDir, vec3 worldNormal, vec3 viewDir, vec3 lightDirNorm, float gloss, mat3 tbn) {
    return calcLightSpecular(gloss, worldNormal, h);
}
",
            Wgsl = @"
// Energy-conserving Blinn-Phong
fn calcLightSpecular(gloss: f32, worldNormal: vec3f, h: vec3f) -> f32 {
    let nh: f32 = max( dot( h, worldNormal ), 0.0 );
    var specPow: f32 = exp2(gloss * 11.0);
    specPow = max(specPow, 0.0001);
    return pow(nh, specPow) * (specPow + 2.0) / 8.0;
}

fn getLightSpecular(h: vec3f, reflDir: vec3f, worldNormal: vec3f, viewDir: vec3f, lightDirNorm: vec3f, gloss: f32, tbn: mat3x3f) -> f32 {
    return calcLightSpecular(gloss, worldNormal, h);
}
"
        },

        // ===== AO DIFFUSE OCCLUSION =====
        new ShaderChunk
        {
            Id = "aoDiffuseOccPS",
            Type = "fragment",
            Description = "Apply AO to diffuse lighting",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
void occludeDiffuse(float ao) {
    dDiffuseLight *= ao;
}
",
            Wgsl = @"
fn occludeDiffuse(ao: f32) {
    dDiffuseLight = dDiffuseLight * ao;
}
"
        },

        // ===== AO SPECULAR OCCLUSION =====
        new ShaderChunk
        {
            Id = "aoSpecOccPS",
            Type = "fragment",
            Description = "Apply AO to specular lighting",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
#if LIT_OCCLUDE_SPECULAR != NONE
    #ifdef LIT_OCCLUDE_SPECULAR_FLOAT
        uniform float material_occludeSpecularIntensity;
    #endif
#endif

void occludeSpecular(float gloss, float ao, vec3 worldNormal, vec3 viewDir) {
    #if LIT_OCCLUDE_SPECULAR == AO
        #ifdef LIT_OCCLUDE_SPECULAR_FLOAT
            float specOcc = mix(1.0, ao, material_occludeSpecularIntensity);
        #else
            float specOcc = ao;
        #endif
    #endif

    #if LIT_OCCLUDE_SPECULAR == GLOSSDEPENDENT
        float specPow = exp2(gloss * 11.0);
        float specOcc = saturate(pow(dot(worldNormal, viewDir) + ao, 0.01 * specPow) - 1.0 + ao);
        #ifdef LIT_OCCLUDE_SPECULAR_FLOAT
            specOcc = mix(1.0, specOcc, material_occludeSpecularIntensity);
        #endif
    #endif

    #if LIT_OCCLUDE_SPECULAR != NONE
        dSpecularLight *= specOcc;
        dReflection *= specOcc;
    #endif
}
",
            Wgsl = @"
#if LIT_OCCLUDE_SPECULAR != NONE
    #ifdef LIT_OCCLUDE_SPECULAR_FLOAT
        uniform material_occludeSpecularIntensity: f32;
    #endif
#endif

fn occludeSpecular(gloss: f32, ao: f32, worldNormal: vec3f, viewDir: vec3f) {

    #if LIT_OCCLUDE_SPECULAR == AO
        #ifdef LIT_OCCLUDE_SPECULAR_FLOAT
            var specOcc: f32 = mix(1.0, ao, uniform.material_occludeSpecularIntensity);
        #else
            var specOcc: f32 = ao;
        #endif
    #endif

    #if LIT_OCCLUDE_SPECULAR == GLOSSDEPENDENT
        var specPow: f32 = exp2(gloss * 11.0);
        var specOcc: f32 = saturate(pow(dot(worldNormal, viewDir) + ao, 0.01 * specPow) - 1.0 + ao);
        #ifdef LIT_OCCLUDE_SPECULAR_FLOAT
            specOcc = mix(1.0, specOcc, uniform.material_occludeSpecularIntensity);
        #endif
    #endif

    #if LIT_OCCLUDE_SPECULAR != NONE
        dSpecularLight = dSpecularLight * specOcc;
        dReflection = dReflection * specOcc;

        #ifdef LIT_SHEEN
            sSpecularLight = sSpecularLight * specOcc;
            sReflection = sReflection * specOcc;
        #endif
    #endif
}
"
        },

        // ===== OUTPUT PS (empty placeholder) =====
        new ShaderChunk
        {
            Id = "outputPS",
            Type = "fragment",
            Description = "Final output (placeholder for custom output logic)",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
// Output placeholder - gl_FragColor is set in endPS/combinePS
"
        },

        // ===== OUTPUT ALPHA =====
        new ShaderChunk
        {
            Id = "outputAlphaPS",
            Type = "fragment",
            Description = "Handle alpha output based on blend type",
            ApiVersion = ApiVersion,
            IsBuiltIn = true,
            Glsl = @"
#if LIT_BLEND_TYPE == NORMAL || LIT_BLEND_TYPE == ADDITIVEALPHA || defined(LIT_ALPHA_TO_COVERAGE)
    gl_FragColor.a = litArgs_opacity;
#elif LIT_BLEND_TYPE == PREMULTIPLIED
    gl_FragColor.rgb *= litArgs_opacity;
    gl_FragColor.a = litArgs_opacity;
#else
    gl_FragColor.a = 1.0;
#endif
",
            Wgsl = @"
#if LIT_BLEND_TYPE == NORMAL || LIT_BLEND_TYPE == ADDITIVEALPHA || defined(LIT_ALPHA_TO_COVERAGE)
    output.color = vec4f(output.color.rgb, litArgs_opacity);
#elif LIT_BLEND_TYPE == PREMULTIPLIED
    output.color = vec4f(output.color.rgb * litArgs_opacity, litArgs_opacity);
#else
    output.color = vec4f(output.color.rgb, 1.0);
#endif
"
        }
    ];
}
