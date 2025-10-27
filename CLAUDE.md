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
```

### Running the Application

```bash
# Run from Visual Studio: F5 or Ctrl+F5
# Or build and run the executable from:
# bin/Release/net9.0-windows10.0.26100.0/TexTool.exe
```

### External Dependencies

The texture conversion pipeline requires the **basisu** CLI tool:
- Installation: `winget install basisu` (Windows)
- Verify: `basisu -version`
- Used by TextureConversion pipeline for Basis Universal compression

## Architecture Overview

### Technology Stack

- **Framework**: .NET 9.0 with Windows SDK 10.0.26100.0
- **UI**: WPF (Windows Presentation Foundation)
- **Pattern**: MVVM with Dependency Injection (Microsoft.Extensions.DependencyInjection)
- **Language**: C# 12 with nullable reference types enabled

### Core Architecture Patterns

**Dependency Injection Setup** (App.xaml.cs:12-24):
- Services registered as singletons (IPlayCanvasService)
- ViewModels registered as transients
- Host-based DI container using Microsoft.Extensions.Hosting

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
- **Core/**: Base types (TextureType, FilterType, CompressionFormat, CompressionSettings)
- **MipGeneration/**: Mipmap generation with multiple filter types and gamma correction
- **BasisU/**: Wrapper for basisu CLI tool (Basis Universal encoder)
- **Pipeline/**: Main conversion pipeline and batch processor

**Key Classes**:
- `TextureConversionPipeline`: Main orchestrator combining mipmap generation and Basis Universal compression
- `MipGenerator`: Generates mipmaps with filter types (Box, Bilinear, Bicubic, Lanczos3, Mitchell, Kaiser)
- `BasisUWrapper`: CLI wrapper for basisu executable
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

### Configuration

**App.config** contains performance settings:
- `DownloadSemaphoreLimit`: Concurrent download limit (default: 8)
- `GetTexturesSemaphoreLimit`: Texture info fetch limit (default: 64)
- `SemaphoreLimit`: General semaphore limit (default: 32)

Adjust based on connection speed (higher for fast connections, lower for slow/unstable).

## Important File Locations

- `MainWindow.xaml.cs`: Main UI window with PlayCanvas connection logic
- `TextureConversionWindow.xaml.cs`: Texture conversion UI
- `App.xaml.cs`: DI container setup
- `Services/PlayCanvasService.cs`: PlayCanvas API client implementation
- `TextureConversion/Pipeline/TextureConversionPipeline.cs`: Main texture processing pipeline
- `TextureConversion/Examples/BasicUsageExample.cs`: Usage examples for conversion pipeline
- `Helpers/`: Utility classes for images, conversions, version info
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

## Common Workflows

### Adding New Texture Conversion Features

1. Define new types/enums in `TextureConversion/Core/`
2. Implement processing logic in `TextureConversion/Pipeline/` or `TextureConversion/MipGeneration/`
3. Update `MipGenerationProfile` or `CompressionSettings` if needed
4. Add usage examples to `TextureConversion/Examples/BasicUsageExample.cs`

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
