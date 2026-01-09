# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

TexTool (PlayCanvas Asset Processor) is a WPF desktop application for working with PlayCanvas projects. It enables connection to PlayCanvas accounts, downloading project files, managing resources (textures, 3D models, materials), and converting textures to Basis Universal format with mipmap generation.

## Build and Development Commands

### Building the Project

```bash
# Build in Release mode
dotnet build TexTool.sln --configuration Release

# Build in Debug mode
dotnet build TexTool.sln --configuration Debug

# Restore NuGet packages
dotnet restore

# Clean build artifacts
dotnet clean

# Build script
.\build-and-run.bat

# Publish optimized Release build (single-file, win-x64 only, with trimming)
dotnet publish AssetProcessor.csproj --configuration Release --runtime win-x64 --self-contained false
```

### Running the Application

```bash
# Run from Visual Studio: F5 or Ctrl+F5
# Or build and run the executable from:
# bin/Release/net9.0-windows10.0.26100.0/win-x64/TexTool.exe
```

### Build Optimizations

Release сборка оптимизирована для уменьшения размера дистрибутива:

- **RuntimeIdentifier=win-x64**: Включаются только Windows x64 библиотеки (удалены linux-x64, osx-x64, win-x86)
- **DebuggerSupport=false**: Удаление отладочных метаданных из Release
- **Optimize=true**: Оптимизации компилятора для производительности
- **StripSymbols=true**: Удаление debug символов

**Важно:** Trimming (PublishTrimmed) **не используется**, так как WPF официально не поддерживает его в .NET 9 (ошибка NETSDK1168).

Подробная информация: [Docs/BuildOptimizations.md](Docs/BuildOptimizations.md)

### External Dependencies

The texture conversion pipeline requires the **ktx** CLI tool from KTX-Software:
- Installation: `winget install KhronosGroup.KTX-Software` (Windows)
- Verify: `ktx --version`
- Used by TextureConversion pipeline via `ktx create` command for Basis Universal compression and KTX2 packing
- Required version: 4.4.0 or higher
- **Important**: We use `ktx.exe` (modern tool), NOT the legacy `toktx.exe`

## Architecture Overview

### Technology Stack

- **Framework**: .NET 9.0 with Windows SDK 10.0.26100.0
- **UI**: WPF (Windows Presentation Foundation)
- **Pattern**: MVVM с ручным составлением зависимостей
- **Language**: C# 12 with nullable reference types enabled

### Core Architecture Patterns

**Составление зависимостей** (App.xaml.cs):
- Главный сервис и окно создаются вручную при старте приложения
- Нет внешнего контейнера внедрения зависимостей

**Service Layer Pattern**:
- `IPlayCanvasService` defines API operations contract
- `PlayCanvasService` implements PlayCanvas REST API communication
- Separates business logic from UI layer

**Resource Models**:
- `BaseResource`: Abstract base for all asset types
- `TextureResource`, `ModelResource`, `MaterialResource`: Specific asset implementations
- Located in Resources/ directory

**MVVM ViewModels**:
- `MainViewModel`: Main window logic, project/branch selection, asset loading
- `TextureConversionViewModel`: Texture conversion settings and batch processing

### Texture Conversion Pipeline

A sophisticated system for texture processing and compression located in `TextureConversion/`:

**Pipeline Components**:
- **Core/**: Base types (TextureType, FilterType, CompressionFormat, CompressionSettings, ToksvigSettings, HistogramSettings)
- **MipGeneration/**: Manual mipmap generation with multiple filter types, gamma correction, and Toksvig processor
- **BasisU/**: Wrapper for ktx CLI tool (KTX2 packing with Basis Universal encoder via `ktx create`)
- **Pipeline/**: Main conversion pipeline with histogram preprocessing and batch processor
- **Settings/**: Advanced conversion settings system with presets and parameter schema
- **Analysis/**: Histogram analysis for dynamic range optimization
- **KVD/**: TLV metadata writer and Ktx2BinaryPatcher for post-processing metadata injection

**Key Classes**:
- `TextureConversionPipeline`: Main orchestrator combining manual mipmap generation, histogram preprocessing, and KTX2 packing
- `MipGenerator`: Generates mipmaps manually with filter types (Box, Bilinear, Bicubic, Lanczos3, Mitchell, Kaiser)
- `ToksvigProcessor`: Applies Toksvig correction for gloss/roughness anti-aliasing
- `KtxCreateWrapper`: CLI wrapper for ktx executable (`ktx create` command for KTX2 packing)
- `HistogramAnalyzer`: Robust percentile-based histogram analysis with soft-knee for dynamic range optimization
- `Ktx2BinaryPatcher`: Direct KTX2 binary format manipulation for metadata injection (Level Index Array updates)
- `BatchProcessor`: Parallel texture processing with progress reporting

**Texture Type Profiles**:
- Albedo: Kaiser filter with gamma correction (sRGB)
- Normal: Kaiser filter with normal normalization, no gamma correction
- Roughness: Kaiser filter, linear space
- Metallic: Box filter (for binary values)
- AO, Emissive: Specialized profiles per TextureConversion/README.md

**Compression Formats**:
- ETC1S: Smaller file size, good for albedo/diffuse
- UASTC: Higher quality, better for normal maps
- Output formats: .basis or .ktx2

### Detailed Texture Processing Workflow

#### Manual vs Automatic Mipmap Generation

The pipeline supports two mipmap generation strategies controlled by `CompressionSettings.UseCustomMipmaps`:

**Manual Mipmap Generation (`UseCustomMipmaps = true`)**:
- `MipGenerator` creates all mipmap levels before ktx create
- Full control over filtering algorithm (Box, Bilinear, Bicubic, Lanczos3, Mitchell, Kaiser)
- Gamma correction applied correctly (convert to linear → filter → convert back to sRGB)
- Energy-preserving filtering for roughness/gloss textures
- **Required for**: Toksvig correction (must analyze and modify each mipmap level)
- **Required for**: Histogram preprocessing with multiple mipmaps
- **Process flow**:
  1. MipGenerator creates all mipmap levels with specified filter
  2. Optional: Apply Toksvig correction to each mipmap level
  3. Optional: Apply histogram preprocessing to all mipmaps
  4. Save all mipmaps as temporary PNGs
  5. Pass all PNG files to ktx create (no `--generate-mipmap` flag)

**Automatic Mipmap Generation (`UseCustomMipmaps = false`)**:
- Pass single source image to ktx create
- ktx create generates mipmaps with `--generate-mipmap --mipmap-filter lanczos4`
- **Required for**: `--normal-mode` and `--normalize` flags to work correctly
- **Limitation**: Cannot apply Toksvig correction or histogram preprocessing to individual mipmap levels
- **Process flow**:
  1. Clone source image (mip0)
  2. Optional: Apply histogram preprocessing to mip0 only
  3. Save mip0 as temporary PNG
  4. Pass single PNG to ktx create with `--generate-mipmap`

**When to use Manual vs Automatic**:
- **Use Manual** for: Albedo, Roughness, Gloss (with Toksvig), any texture requiring histogram preprocessing
- **Use Automatic** for: Normal maps (benefit from `--normal-mode` and `--normalize`)

#### ktx create Command Line Parameters

The `KtxCreateWrapper` generates ktx create commands with the following parameters:

**Format Specification** (required):
```
--format R8G8B8A8_SRGB   # For sRGB color space textures
--format R8G8B8A8_UNORM  # For linear color space textures
```

**Encoding** (required for Basis Universal):
```
--encode uastc    # UASTC format (high quality)
--encode basis-lz # ETC1S/BasisLZ format (smaller size)
```

**Mipmap Generation** (automatic mode only):
```
--generate-mipmap              # Generate mipmaps automatically
--mipmap-filter lanczos4       # Filter type (lanczos4 default)
```

**Compression Parameters**:

ETC1S/BasisLZ:
```
--clevel 1        # Compression level (0-5, default 1)
--qlevel 128      # Quality level (1-255, default 128)
```

UASTC:
```
--uastc-quality 2          # Quality level (0-4, default 2)
--uastc-rdo                # Enable RDO optimization
--uastc-rdo-l 1.0          # RDO lambda (0.001-10.0)
```

**Supercompression** (Zstandard):
```
--zstd 3          # Zstd compression level (1-22)
```
**CRITICAL**: Zstd is ONLY supported with UASTC, NOT with ETC1S/BasisLZ!

**Normal Map Processing**:
```
--normal-mode     # Convert to XY(RGB/A) normal map layout
--normalize       # Normalize normal vectors
```

**Threading**:
```
--threads 8       # Number of threads (0 = auto)
```

**Input/Output Order**:
```
ktx create [options] input1.png input2.png ... output.ktx2
```
Input files BEFORE output file!

#### Histogram Preprocessing Pipeline

When `CompressionSettings.HistogramAnalysis` is set (not null and Mode != Off), the pipeline ALWAYS applies preprocessing:

**Step 1: Analysis**
- Analyze mip0 to compute robust percentile-based range
- Modes: `PercentileWithKnee` (High Quality) or `Percentile` (Fast)
- Returns scale/offset for normalization

**Step 2: Normalization (Preprocessing)**
- Apply transformation to ALL mipmaps:
  - `PercentileWithKnee`: Soft-knee smoothstep for outliers
  - `Percentile`: Hard winsorization clamping
- Texture normalized to [0, 1] range
- Better utilizes compressed texture precision

**Step 3: Inverse Transform for GPU**
- Compute inverse scale/offset for GPU denormalization:
  ```
  scale_inv = 1.0 / scale
  offset_inv = -offset / scale
  ```
- GPU applies: `v_original = v_normalized * scale_inv + offset_inv`

**Step 4: Metadata Writing**
- Create TLV (Type-Length-Value) metadata with:
  - Histogram result (HIST_SCALAR or HIST_PER_CHANNEL_3)
  - Histogram parameters (percentiles, knee width)
- Quantization: Always Half16 (4 bytes per channel)
- Save to `pc.meta.bin` file

**Step 5: Post-Processing Injection**
- ktx create doesn't support KVD via CLI
- `Ktx2BinaryPatcher` injects metadata after ktx create:
  1. Load KTX2 file
  2. Insert KVD data section
  3. Update Level Index Array offsets
  4. Rewrite file with correct headers

**Quality Modes**:
- **HighQuality** (recommended): PercentileWithKnee (0.5%, 99.5%), knee=2%, soft-knee smoothing
- **Fast**: Percentile (1%, 99%), hard clamping without soft-knee

**Channel Modes**:
- **AverageLuminance**: Single scale/offset for all channels (4 bytes metadata)
- **PerChannel**: Separate scale/offset for RGB (12 bytes metadata)

#### Filter Types and Gamma Correction

**Available Filters** (`MipGenerator`):
- **Box**: Fast, blocky results (for metallic maps)
- **Bilinear**: Linear interpolation, soft
- **Bicubic**: Cubic interpolation, sharper than bilinear
- **Lanczos3**: 3-lobe Lanczos, good quality/performance
- **Mitchell**: Mitchell-Netravali filter, balanced
- **Kaiser**: High-quality filter (recommended for albedo)

**Gamma Correction** (for sRGB textures):
- Applied during manual mipmap generation
- Process: sRGB → Linear → Filter → Linear → sRGB
- Default gamma: 2.2
- Prevents color shifts during downsampling

**Energy-Preserving Filtering** (for roughness/gloss):
- Computes variance of filtered normals
- Adjusts roughness to preserve specular energy
- Prevents excessive specular aliasing at distance

### Configuration

**App.config** contains performance settings:
- `DownloadSemaphoreLimit`: Concurrent download limit (default: 8)
- `GetTexturesSemaphoreLimit`: Texture info fetch limit (default: 64)
- `SemaphoreLimit`: General semaphore limit (default: 32)

Adjust based on connection speed (higher for fast connections, lower for slow/unstable).

## Important File Locations

### Core Application
- `MainWindow.xaml.cs`: Main UI window with PlayCanvas connection logic
- `MainWindow.Models.cs`: Model export UI handlers
- `App.xaml.cs`: DI container setup and theme initialization
- `Settings/AppSettings.cs`: All application settings including B2/CDN

### Texture Processing
- `TextureConversion/Pipeline/TextureConversionPipeline.cs`: Main texture processing pipeline
- `TextureConversion/MipGeneration/ToksvigProcessor.cs`: Toksvig correction implementation
- `TextureConversion/Settings/ConversionSettingsManager.cs`: Advanced settings management
- `TextureViewer/`: GPU-based texture viewer with D3D11 rendering

### Model Export
- `Export/ModelExportPipeline.cs`: Complete model export workflow
- `Export/ExportOptions.cs`: Export configuration
- `Mapping/`: Mapping JSON generators (MaterialJsonGenerator, etc.)

### CDN/Upload
- `Upload/B2UploadService.cs`: Backblaze B2 API client (625 lines)
- `Upload/IB2UploadService.cs`: Interface and upload models
- `Upload/B2UploadSettings.cs`: Settings and result classes
- `Services/AssetUploadCoordinator.cs`: Upload orchestration

### Resources
- `Resources/BaseResource.cs`: Abstract base with upload status properties
- `Resources/TextureResource.cs`: Texture-specific properties
- `Resources/ModelResource.cs`: Model resource

### UI Components
- `Windows/PresetEditorWindow.xaml.cs`: Texture conversion preset editor
- `Windows/PresetManagementWindow.xaml.cs`: Preset management interface
- `Controls/ORMPackingPanel.xaml.cs`: ORM texture packing UI
- `Controls/LogViewerControl.xaml.cs`: Log viewer with filtering

### Helpers & Utilities
- `Helpers/MainWindowHelpers.cs`: Histogram calculation with statistics
- `Helpers/ThemeHelper.cs`: Windows theme detection
- `Exceptions/`: Custom exception types for API, network, file integrity errors

## Key Development Notes

### Git Integration

The project captures git branch and commit info during build (AssetProcessor.csproj:17-32) using MSBuild targets. This information is made available to the compiler via `CompilerVisibleProperty`.

### NuGet Package Sources

The project uses a local `nuget.config` to avoid global PackageSourceMapping conflicts. If package restore fails:
```bash
dotnet nuget locals all --clear
dotnet restore
```

### Logging

Uses NLog with configuration in `NLog.config`. Logs are written to `file.txt`. Log levels: Info (general operations), Warn (missing settings), Error (failures).

### PlayCanvas API Authentication

Requires API key from playcanvas.com/account → API Tokens. Settings stored in App.config user settings:
- Username
- PlaycanvasApiKey
- ProjectsFolderPath

### Concurrency and Performance

Heavy use of async/await throughout codebase. Semaphore limits in App.config control concurrent operations. PlayCanvasService uses SemaphoreSlim for throttling API requests.

### TextureViewer

GPU-based texture viewer using Direct3D11 (Vortice.Windows):
- Supports PNG and KTX2 formats with full mipmap chains
- Real-time mipmap level selection (Auto/Fixed modes)
- Multiple filtering modes (Point/Linear/Anisotropic)
- sRGB/Linear color space switching
- Zoom, pan, and pixel inspection capabilities
- Uses `libktx` P/Invoke for KTX2 loading with Basis Universal transcoding

Located in `TextureViewer/` directory. See `Docs/TextureViewerSpec.md` for detailed specifications.

### Histogram Statistics

Histogram calculation in `MainWindowHelpers.cs` uses thread-local strategy to avoid race conditions:
- Each thread maintains local histograms during parallel processing
- Results merged with locking for thread-safety
- Calculates Min, Max, Mean, Median, StdDev, and pixel count
- Deterministic results (same texture always produces identical statistics)

## Common Workflows

### Adding New Texture Conversion Features

1. Define new types/enums in `TextureConversion/Core/`
2. Implement processing logic in `TextureConversion/Pipeline/` or `TextureConversion/MipGeneration/`
3. Update `MipGenerationProfile` or `CompressionSettings` if needed
4. Add parameters to `ConversionSettingsSchema.cs` if UI configuration needed
5. Update preset definitions in `ConversionSettingsSchema.GetPredefinedPresets()`
6. Add usage examples to `TextureConversion/Examples/BasicUsageExample.cs`

### Working with Texture Conversion Presets

1. Presets defined in `TextureConversion/Settings/ConversionSettingsSchema.cs`
2. UI editing in `Windows/PresetEditorWindow.xaml.cs`
3. Parameter visibility controlled by `VisibilityCondition` (e.g., UASTC options only visible when UASTC format selected)
4. CLI argument generation in `KtxCreateWrapper.GenerateKtxCreateArguments()`
5. Internal preprocessing parameters (Toksvig, mipmap generation, histogram) handled before ktx create invocation

### Using Histogram Preprocessing

Histogram preprocessing is a powerful feature for optimizing texture compression by normalizing dynamic range.

**Basic Usage (High Quality - Recommended)**:
```csharp
var settings = new CompressionSettings {
    CompressionFormat = CompressionFormat.ETC1S,
    QualityLevel = 128,
    GenerateMipmaps = true,
    UseCustomMipmaps = true,  // Required for histogram preprocessing

    // Enable histogram preprocessing with High Quality mode
    HistogramAnalysis = HistogramSettings.CreateHighQuality()
    // Uses: PercentileWithKnee (0.5%, 99.5%), knee=2%, soft-knee smoothing
};
```

**Fast Mode (Lower Quality, Faster)**:
```csharp
var settings = new CompressionSettings {
    CompressionFormat = CompressionFormat.ETC1S,
    QualityLevel = 128,
    UseCustomMipmaps = true,

    // Fast mode with hard clamping
    HistogramAnalysis = HistogramSettings.CreateFast()
    // Uses: Percentile (1%, 99%), no soft-knee
};
```

**Per-Channel Mode (for color-rich textures)**:
```csharp
var settings = new CompressionSettings {
    CompressionFormat = CompressionFormat.ETC1S,
    QualityLevel = 192,
    UseCustomMipmaps = true,

    HistogramAnalysis = new HistogramSettings {
        Quality = HistogramQuality.HighQuality,
        Mode = HistogramMode.PercentileWithKnee,
        ChannelMode = HistogramChannelMode.PerChannel,  // Separate scale/offset for R, G, B
        PercentileLow = 0.5f,
        PercentileHigh = 99.5f,
        KneeWidth = 0.02f
    }
};
```

**UI Configuration**:
- Open Texture Conversion Settings panel
- Enable "Enable Histogram Preprocessing" checkbox
- Select Quality Mode: "HighQuality" or "Fast"
- Select Channel Mode: "AverageLuminance" (default) or "PerChannel"
- Adjust sliders if needed (percentiles, knee width)

**Important Notes**:
- Histogram preprocessing ALWAYS normalizes the texture (preprocessing mode)
- Scale/offset are ALWAYS written to KTX2 metadata for GPU recovery
- Metadata format: Always Half16 (4 bytes per channel)
- GPU shader must read metadata and apply: `v_original = fma(v_normalized, scale, offset)`
- Use `UseCustomMipmaps = true` when histogram preprocessing is enabled
- Metadata injected via `Ktx2BinaryPatcher` post-processing (ktx create doesn't support KVD)

**Recommended Settings by Texture Type**:
- **Albedo**: HighQuality, AverageLuminance
- **HDR textures**: HighQuality with conservative percentiles (0.1%, 99.9%)
- **Emissive**: Fast, PerChannel (for colored light sources)
- **Roughness/Metallic**: Fast, AverageLuminance (narrow range)
- **Normal maps**: Disable histogram (use standard normalization)

### Working with PlayCanvas API

1. Add method signature to `IPlayCanvasService`
2. Implement in `PlayCanvasService` using HttpClient
3. Handle errors with custom exceptions from `Exceptions/`
4. Use semaphore throttling for concurrent requests

### UI Development

- XAML files colocated with .xaml.cs code-behind
- Use data binding to ViewModels (MVVM pattern)
- WPF controls from Extended.Wpf.Toolkit, HelixToolkit.Wpf, OxyPlot.Wpf

## Project-Specific Conventions

- Code style defined in `.editorconfig`: 4 spaces, CRLF line endings, UTF-8 with BOM
- Nullable reference types enabled project-wide
- Naming: PascalCase for types/methods, camelCase for fields
- Comments and documentation in Russian (README, code comments)
- Image processing uses SixLabors.ImageSharp 3.1.11

---

## CDN/Upload Pipeline

### Overview

The application supports uploading converted assets to Backblaze B2 cloud storage for CDN delivery.

### B2 Upload Service

Located in `Upload/B2UploadService.cs`:
- Full Backblaze B2 Native API v2 integration
- SHA1 hash-based file integrity verification
- Concurrent upload capability (configurable, default 4)
- Smart upload URL caching with automatic refresh
- Skip-existing-files optimization via hash comparison

**Key Methods**:
```csharp
AuthorizeAsync()           // B2 account authorization
UploadFileAsync()          // Single file upload with hash verification
UploadBatchAsync()         // Parallel batch upload with progress
ListFilesAsync()           // List files in bucket with pagination
FileExistsAsync()          // Check if file exists by prefix
DeleteFileAsync()          // Delete file from B2
```

### Asset Upload Coordinator

Located in `Services/AssetUploadCoordinator.cs`:
- Orchestrates upload operations
- SHA1-based upload necessity checking
- Resource status event publishing
- Remote path construction based on file type

### Upload Status Tracking

BaseResource includes upload properties:
```csharp
UploadStatus      // "Queued", "Uploading", "Uploaded", "Upload Failed", "Upload Outdated"
UploadedHash      // SHA1 hash of uploaded file
LastUploadedAt    // Timestamp of last upload
RemoteUrl         // CDN URL for the file
UploadProgress    // 0-100 percentage
```

**IMPORTANT**: Upload state is currently stored in-memory only and is lost on application restart. See `Docs/PIPELINE_REVIEW.md` for planned persistence improvements.

### CDN Settings

Settings stored in AppSettings:
- `B2KeyId` - Application Key ID
- `B2ApplicationKey` - Application Key (encrypted)
- `B2BucketName` - Bucket name
- `B2PathPrefix` - Path prefix in bucket
- `CdnBaseUrl` - Base CDN URL
- `B2MaxConcurrentUploads` - Max parallel uploads (default: 4)

---

## Model Export Pipeline

### Overview

Located in `Export/ModelExportPipeline.cs`. Exports PlayCanvas models with all associated textures and materials to a deployable format.

### Export Structure

```
{project}/server/
├── mapping.json                              # Global asset mapping
└── assets/content/{model_folder}/
    ├── textures/                             # Converted KTX2 textures
    │   ├── albedo.ktx2
    │   ├── normal.ktx2
    │   └── orm_{materialId}.ktx2             # Packed ORM textures
    ├── materials/
    │   └── {material}.json                   # Material definitions
    ├── {model}.glb                           # Base model
    ├── {model}_lod1.glb, _lod2.glb           # LOD variants
    └── {model}.json                          # Model manifest
```

### Export Workflow

1. **Determine folder path** from PlayCanvas hierarchy
2. **Find materials** for model (by Parent ID, folder path, name matching)
3. **Collect textures** from materials
4. **ORM Packing** - generate packed ORM textures (AO, Gloss, Metallic)
5. **KTX2 Conversion** - convert textures to compressed KTX2
6. **Material JSON** - generate per-material JSON with texture references
7. **Model JSON** - generate model JSON with LODs and material IDs
8. **GLB Conversion** - convert FBX to GLB with optional LOD generation
9. **Mapping Update** - update global `mapping.json`

### Key Classes

- `ModelExportPipeline` - Main orchestrator
- `ExportOptions` - Controls what gets exported
- `ModelExportResult` - Export completion status
- `MappingData` - Global mapping.json structure

### Mapping.json Format

```json
{
  "Models": {
    "123": { "Path": "assets/content/model/model.json" }
  },
  "Materials": {
    "456": { "Path": "assets/content/model/materials/mat.json" }
  },
  "Textures": {
    "789": { "Path": "assets/content/model/textures/albedo.ktx2" },
    "-456": { "Path": "assets/content/model/textures/orm_456.ktx2" }  // ORM uses negative material ID
  }
}
```

---

## Known Issues and Planned Improvements

See `Docs/PIPELINE_REVIEW.md` for:
- Detailed pipeline architecture review
- List of unused/obsolete code
- Comprehensive improvement plan
- Prioritized feature roadmap
