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

Release сборка оптимизирована для уменьшения размера и количества DLL:

- **RuntimeIdentifier=win-x64**: Включаются только Windows x64 библиотеки (удалены linux-x64, osx-x64, win-x86)
- **PublishTrimmed=true**: Удаление неиспользуемого кода из всех сборок
- **TrimMode=partial**: Безопасный режим trimming для WPF приложений
- **DebuggerSupport=false**: Удаление отладочных метаданных из Release
- **Защита WPF-зависимостей**: TrimmerRootAssembly для критичных библиотек

Подробная информация: [Docs/BuildOptimizations.md](Docs/BuildOptimizations.md)

### External Dependencies

The texture conversion pipeline requires the **toktx** CLI tool from KTX-Software:
- Installation: `winget install KhronosGroup.KTX-Software` (Windows)
- Verify: `toktx --version`
- Used by TextureConversion pipeline for Basis Universal compression and KTX2 packing
- Required version: 4.3.0 or higher

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
- **Core/**: Base types (TextureType, FilterType, CompressionFormat, CompressionSettings, ToksvigSettings)
- **MipGeneration/**: Mipmap generation with multiple filter types, gamma correction, and Toksvig processor
- **BasisU/**: Wrapper for toktx CLI tool (KTX2 packing with Basis Universal encoder)
- **Pipeline/**: Main conversion pipeline and batch processor
- **Settings/**: Advanced conversion settings system with presets and parameter schema

**Key Classes**:
- `TextureConversionPipeline`: Main orchestrator combining mipmap generation and KTX2 packing
- `MipGenerator`: Generates mipmaps with filter types (Box, Bilinear, Bicubic, Lanczos3, Mitchell, Kaiser)
- `ToksvigProcessor`: Applies Toksvig correction for gloss/roughness anti-aliasing
- `ToktxWrapper`: CLI wrapper for toktx executable (KTX2 packing)
- `BatchProcessor`: Parallel texture processing with progress reporting
- `ConversionSettingsManager`: Manages conversion parameters and generates toktx arguments

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

### Configuration

**App.config** contains performance settings:
- `DownloadSemaphoreLimit`: Concurrent download limit (default: 8)
- `GetTexturesSemaphoreLimit`: Texture info fetch limit (default: 64)
- `SemaphoreLimit`: General semaphore limit (default: 32)

Adjust based on connection speed (higher for fast connections, lower for slow/unstable).

## Important File Locations

- `MainWindow.xaml.cs`: Main UI window with PlayCanvas connection logic
- `Windows/PresetEditorWindow.xaml.cs`: Texture conversion preset editor
- `Windows/PresetManagementWindow.xaml.cs`: Preset management interface
- `App.xaml.cs`: DI container setup
- `Services/PlayCanvasService.cs`: PlayCanvas API client implementation
- `TextureConversion/Pipeline/TextureConversionPipeline.cs`: Main texture processing pipeline
- `TextureConversion/MipGeneration/ToksvigProcessor.cs`: Toksvig correction implementation
- `TextureConversion/Settings/ConversionSettingsManager.cs`: Advanced settings management
- `TextureViewer/`: GPU-based texture viewer with D3D11 rendering
- `Helpers/MainWindowHelpers.cs`: Histogram calculation with statistics (Min/Max/Mean/Median/StdDev)
- `Exceptions/`: Custom exception types for API, network, file integrity errors

## Key Development Notes

### Git Integration

The project captures git branch and commit info during build (AssetProcessor.csproj:17-32) using MSBuild targets. This information is made available to the compiler via `CompilerVisibleProperty`.

### Unity Editor Integration

`PlayCanvasImporterWindow.cs` is excluded from WPF compilation (AssetProcessor.csproj:42-43) as it's meant for Unity Editor only. It imports PlayCanvas JSON scenes into Unity.

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
4. CLI argument generation in `ConversionSettingsManager.GenerateToktxArguments()`
5. Internal preprocessing parameters (Toksvig, mipmap generation) handled before toktx invocation

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
