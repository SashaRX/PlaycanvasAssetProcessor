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
        }
    ];
}
