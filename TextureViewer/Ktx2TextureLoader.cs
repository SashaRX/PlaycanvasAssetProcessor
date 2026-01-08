using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace AssetProcessor.TextureViewer;

/// <summary>
/// Loads KTX2 textures via libktx and transcodes Basis Universal to RGBA8.
/// NOTE: Uses Trace.WriteLine instead of NLog to avoid deadlocks when called from Task.Run
/// </summary>
public static class Ktx2TextureLoader {

    // Lock object for libktx calls - libktx may not be fully thread-safe
    private static readonly object _libktxLock = new object();

    /// <summary>
    /// Load a KTX2 texture from a file path.
    /// </summary>
    public static TextureData LoadFromFile(string filePath) {
        // Diagnostic logging disabled
        void DiagLog(string msg) { }

        DiagLog($"[KTX2LOADER] Loading KTX2: {filePath}");

        // Read entire file into memory first to avoid file locking issues
        DiagLog("[KTX2LOADER] Reading file bytes...");
        byte[] fileData = File.ReadAllBytes(filePath);
        DiagLog($"[KTX2LOADER] File read: {fileData.Length} bytes");

        // Parse metadata from memory buffer (no libktx involved)
        DiagLog("[KTX2LOADER] Parsing metadata...");
        var (histogramMetadata, normalLayoutMetadata) = Ktx2MetadataReader.ReadAllMetadataFromMemory(fileData);
        DiagLog("[KTX2LOADER] Metadata parsed");

        // КРИТИЧНО: Загружаем ktx.dll перед использованием P/Invoke
        DiagLog("[KTX2LOADER] Loading ktx.dll...");
        if (!LibKtxNative.LoadKtxDll()) {
            Trace.WriteLine("[KTX2LOADER] ERROR: Failed to load ktx.dll");
            throw new DllNotFoundException("Unable to load ktx.dll");
        }
        DiagLog("[KTX2LOADER] ktx.dll loaded");

        // Lock all libktx operations - the library may not be thread-safe
        DiagLog("[KTX2LOADER] Acquiring _libktxLock...");
        lock (_libktxLock) {
            DiagLog("[KTX2LOADER] Lock acquired");
            IntPtr dataPtr = Marshal.AllocHGlobal(fileData.Length);
            try {
                DiagLog("[KTX2LOADER] Copying to unmanaged memory...");
                Marshal.Copy(fileData, 0, dataPtr, fileData.Length);
                DiagLog("[KTX2LOADER] Calling ktxTexture2_CreateFromMemory...");

                uint createFlags = (uint)LibKtxNative.KtxTextureCreateFlagBits.KTX_TEXTURE_CREATE_LOAD_IMAGE_DATA_BIT |
                                  (uint)LibKtxNative.KtxTextureCreateFlagBits.KTX_TEXTURE_CREATE_RAW_KVDATA_BIT;
                var result = LibKtxNative.ktxTexture2_CreateFromMemory(
                    dataPtr,
                    new UIntPtr((uint)fileData.Length),
                    createFlags,
                    out IntPtr textureHandle);

                DiagLog($"[KTX2LOADER] CreateFromMemory returned: {result}");

                if (result != LibKtxNative.KtxErrorCode.KTX_SUCCESS) {
                    throw new Exception($"Failed to load KTX2: {LibKtxNative.GetErrorString(result)}");
                }

                try {
                    DiagLog("[KTX2LOADER] Calling LoadFromHandle...");
                    var textureData = LoadFromHandle(textureHandle, filePath, histogramMetadata, normalLayoutMetadata);
                    DiagLog($"[KTX2LOADER] KTX2 loaded: {textureData.Width}x{textureData.Height}, {textureData.MipCount} mips");
                    return textureData;
                } finally {
                    DiagLog("[KTX2LOADER] Destroying texture handle...");
                    LibKtxNative.ktxTexture2_Destroy(textureHandle);
                }
            } finally {
                DiagLog("[KTX2LOADER] Freeing unmanaged memory...");
                Marshal.FreeHGlobal(dataPtr);
            }
        }
    }

    private static string FindKtxExecutable() {
        // Common installation paths
        string[] searchPaths = {
            @"C:\Program Files\KTX-Software\bin\ktx.exe",
            @"C:\Program Files (x86)\KTX-Software\bin\ktx.exe",
            "ktx.exe", // Try PATH
            "ktx" // Try PATH without .exe
        };

        foreach (var path in searchPaths) {
            try {
                if (Path.IsPathRooted(path)) {
                    if (File.Exists(path)) {
                        Trace.WriteLine($"[KTX2LOADER] Found ktx.exe at: {path}");
                        return path;
                    }
                } else {
                    // Try to find in PATH
                    var psi = new System.Diagnostics.ProcessStartInfo {
                        FileName = path,
                        Arguments = "--version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = System.Diagnostics.Process.Start(psi);
                    if (process != null) {
                        // Читаем stdout/stderr ПЕРЕД WaitForExit чтобы избежать deadlock
                        process.StandardOutput.ReadToEnd();
                        process.StandardError.ReadToEnd();
                        process.WaitForExit();
                        if (process.ExitCode == 0 || process.ExitCode == 1) { // Some versions return 1 for --version
                            Trace.WriteLine($"[KTX2LOADER] Found ktx via PATH: {path}");
                            return path;
                        }
                    }
                }
            } catch {
                // Continue searching
            }
        }

        throw new Exception("ktx.exe not found. Please install KTX-Software or add ktx to PATH.");
    }

    private static TextureData LoadViaKtxExtract(string filePath) {
        Trace.WriteLine($"[KTX2LOADER] Loading KTX2 via ktx extract tool: {filePath}");

        // Create temp directory
        string tempDir = Path.Combine(Path.GetTempPath(), "ktx_extract_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try {
            string outputBase = Path.Combine(tempDir, "mip");

            // Run ktx extract command
            // Try to find ktx.exe in common locations
            string ktxExe = FindKtxExecutable();

            var psi = new System.Diagnostics.ProcessStartInfo {
                FileName = ktxExe,
                Arguments = $"extract --level all --transcode rgba8 \"{filePath}\" \"{outputBase}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Trace.WriteLine($"[KTX2LOADER] Running: ktx extract --level all --transcode rgba8 \"{filePath}\" \"{outputBase}\"");

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) {
                throw new Exception("Failed to start ktx process");
            }

            // КРИТИЧНО: Читаем оба потока ПАРАЛЛЕЛЬНО чтобы избежать deadlock
            var stdoutBuilder = new System.Text.StringBuilder();
            var stderrBuilder = new System.Text.StringBuilder();

            process.OutputDataReceived += (sender, e) => {
                if (e.Data != null) stdoutBuilder.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (sender, e) => {
                if (e.Data != null) stderrBuilder.AppendLine(e.Data);
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            string stdout = stdoutBuilder.ToString();
            string stderr = stderrBuilder.ToString();

            if (process.ExitCode != 0) {
                Trace.WriteLine($"[KTX2LOADER] ERROR: ktx extract failed with exit code {process.ExitCode}");
                if (!string.IsNullOrEmpty(stderr)) Trace.WriteLine($"[KTX2LOADER] ERROR: stderr: {stderr}");
                throw new Exception($"ktx extract failed with exit code {process.ExitCode}");
            }

            // Find all extracted PNG files
            var pngFiles = Directory.GetFiles(tempDir, "*.png")
                .OrderBy(f => f)
                .ToList();

            if (pngFiles.Count == 0) {
                throw new Exception("No PNG files extracted by ktx");
            }

            Trace.WriteLine($"[KTX2LOADER] Found {pngFiles.Count} extracted PNG files");

            // Load first PNG as base mip
            var baseImage = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(pngFiles[0]);
            int width = baseImage.Width;
            int height = baseImage.Height;

            var mipLevels = new List<MipLevel>();

            // Load all mips
            for (int i = 0; i < pngFiles.Count; i++) {
                using var img = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(pngFiles[i]);

                byte[] mipData = new byte[img.Width * img.Height * 4];
                img.ProcessPixelRows(accessor => {
                    for (int y = 0; y < img.Height; y++) {
                        var row = accessor.GetRowSpan(y);
                        int rowOffset = y * img.Width * 4;
                        for (int x = 0; x < img.Width; x++) {
                            var pixel = row[x];
                            int pixelOffset = rowOffset + x * 4;
                            mipData[pixelOffset] = pixel.R;
                            mipData[pixelOffset + 1] = pixel.G;
                            mipData[pixelOffset + 2] = pixel.B;
                            mipData[pixelOffset + 3] = pixel.A;
                        }
                    }
                });

                mipLevels.Add(new MipLevel {
                    Level = i,
                    Width = img.Width,
                    Height = img.Height,
                    Data = mipData,
                    RowPitch = img.Width * 4
                });
            }

            return new TextureData {
                Width = width,
                Height = height,
                MipLevels = mipLevels,
                IsSRGB = true,
                HasAlpha = true,
                SourceFormat = $"KTX2 (via ktx extract)",
                IsHDR = false,
                IsCompressed = false,
                CompressionFormat = null
            };
        } finally {
            // Cleanup temp directory
            try {
                Directory.Delete(tempDir, true);
            } catch {
                // Ignore cleanup errors
            }
        }
    }

    /// <summary>
    /// Load a KTX2 texture from a byte array.
    /// </summary>
    public static TextureData LoadFromMemory(byte[] data) {
        Trace.WriteLine($"[KTX2LOADER] Loading KTX2 texture from memory ({data.Length} bytes)");

        // КРИТИЧНО: Загружаем ktx.dll перед использованием P/Invoke
        if (!LibKtxNative.LoadKtxDll()) {
            Trace.WriteLine("[KTX2LOADER] ERROR: Failed to load ktx.dll. Cannot load KTX2 files.");
            throw new DllNotFoundException("Unable to load ktx.dll. Please ensure KTX-Software is installed and ktx.dll is available.");
        }

        IntPtr dataPtr = Marshal.AllocHGlobal(data.Length);
        try {
            Marshal.Copy(data, 0, dataPtr, data.Length);

            var result = LibKtxNative.ktxTexture2_CreateFromMemory(
                dataPtr,
                new UIntPtr((uint)data.Length),
                (uint)LibKtxNative.KtxTextureCreateFlagBits.KTX_TEXTURE_CREATE_LOAD_IMAGE_DATA_BIT,
                out IntPtr textureHandle);

            if (result != LibKtxNative.KtxErrorCode.KTX_SUCCESS) {
                throw new Exception($"Failed to load KTX2 from memory: {LibKtxNative.GetErrorString(result)}");
            }

            try {
                return LoadFromHandle(textureHandle, ""); // No filePath for memory loading
            } finally {
                LibKtxNative.ktxTexture2_Destroy(textureHandle);
            }
        } finally {
            Marshal.FreeHGlobal(dataPtr);
        }
    }

    /// <summary>
    /// Helper to decode Vulkan format codes to human-readable names.
    /// </summary>
    private static string GetVkFormatName(uint vkFormat) {
        // Also check if the value looks like byte-swapped (common structure alignment issue)
        uint swapped = ((vkFormat & 0xFF) << 24) | ((vkFormat & 0xFF00) << 8) |
                       ((vkFormat & 0xFF0000) >> 8) | ((vkFormat & 0xFF000000) >> 24);

        return vkFormat switch {
            0 => "UNDEFINED",
            37 => "R8G8B8A8_SRGB",
            43 => "B8G8R8A8_SRGB",
            44 => "R8G8B8A8_UNORM",
            50 => "B8G8R8A8_UNORM",
            146 => "BC7_SRGB_BLOCK",
            145 => "BC7_UNORM_BLOCK",
            134 => "BC1_RGBA_SRGB_BLOCK",
            133 => "BC1_RGBA_UNORM_BLOCK",
            _ => swapped switch {
                37 => $"R8G8B8A8_SRGB (byte-swapped: {vkFormat})",
                43 => $"B8G8R8A8_SRGB (byte-swapped: {vkFormat})",
                44 => $"R8G8B8A8_UNORM (byte-swapped: {vkFormat})",
                50 => $"B8G8R8A8_UNORM (byte-swapped: {vkFormat})",
                146 => $"BC7_SRGB_BLOCK (byte-swapped: {vkFormat})",
                145 => $"BC7_UNORM_BLOCK (byte-swapped: {vkFormat})",
                _ => $"UNKNOWN({vkFormat}, swapped={swapped})"
            }
        };
    }

    /// <summary>
    /// Load texture data from a ktxTexture2 handle.
    /// </summary>
    private static TextureData LoadFromHandle(IntPtr textureHandle, string filePath, HistogramMetadata? histogramMetadata = null, NormalLayoutMetadata? normalLayoutMetadata = null) {
        // Diagnostic logging disabled
        void DiagLog(string msg) { }

        DiagLog("[LoadFromHandle] ENTRY");

        // Read basic texture info from structure (minimal fields only)
        var tex = Marshal.PtrToStructure<LibKtxNative.KtxTexture2>(textureHandle);

        DiagLog($"[LoadFromHandle] KTX2: {tex.baseWidth}x{tex.baseHeight}, {tex.numLevels} levels, compressed={tex.isCompressed}");

        // Read OETF (transfer function) from DFD to determine sRGB vs linear
        DiagLog("[LoadFromHandle] Getting OETF...");
        uint oetf = LibKtxNative.ktxTexture2_GetOETF(textureHandle);
        var transfer = (LibKtxNative.KhrDfTransfer)oetf;
        bool isSRGB = transfer switch {
            LibKtxNative.KhrDfTransfer.KHR_DF_TRANSFER_SRGB => true,       // sRGB
            LibKtxNative.KhrDfTransfer.KHR_DF_TRANSFER_ITU => true,        // BT.709 (same as sRGB)
            LibKtxNative.KhrDfTransfer.KHR_DF_TRANSFER_LINEAR => false,    // Linear
            _ => true  // Default to sRGB for safety (unspecified/unknown)
        };
        DiagLog($"[LoadFromHandle] OETF={oetf}, isSRGB={isSRGB}");

        // Check if needs transcoding (Basis Universal compressed)
        DiagLog("[LoadFromHandle] Checking NeedsTranscoding...");
        bool needsTranscode = LibKtxNative.ktxTexture2_NeedsTranscoding(textureHandle);
        DiagLog($"[LoadFromHandle] NeedsTranscoding={needsTranscode}");
        string? transcodeFormat = null; // Track what format we transcoded to

        if (needsTranscode) {
            DiagLog("[LoadFromHandle] Transcoding Basis Universal to BC3...");

            // Transcode Basis Universal to BC3_RGBA
            // libktx vcpkg v4.4.2 supports BC3, BC7 may not work properly
            var transcodeResult = LibKtxNative.ktxTexture2_TranscodeBasis(
                textureHandle,
                LibKtxNative.KtxTranscodeFormat.KTX_TTF_BC3_RGBA,
                0);

            DiagLog($"[LoadFromHandle] TranscodeBasis returned: {transcodeResult}");
            if (transcodeResult != LibKtxNative.KtxErrorCode.KTX_SUCCESS) {
                throw new Exception($"Failed to transcode Basis Universal texture: {LibKtxNative.GetErrorString(transcodeResult)}");
            }

            // We KNOW we transcoded to BC3 - use sRGB variant based on source VkFormat
            transcodeFormat = isSRGB ? "BC3_SRGB_BLOCK" : "BC3_UNORM_BLOCK";
            DiagLog($"[LoadFromHandle] Transcoded to: {transcodeFormat}");

            // Re-read structure after transcode
            tex = Marshal.PtrToStructure<LibKtxNative.KtxTexture2>(textureHandle);
        }

        // Extract mip levels using iterator callback
        // This is the safest way that works regardless of internal format
        DiagLog("[LoadFromHandle] Starting mip iteration...");
        var mipLevels = new List<MipLevel>();

        LibKtxNative.KtxIterCallback callback = (int miplevel, int face, int width, int height, int depth, UIntPtr imageSize, IntPtr pixels, IntPtr userdata) => {
            int size = (int)imageSize;

            // Copy pixel data
            byte[] mipData = new byte[size];
            Marshal.Copy(pixels, mipData, 0, size);

            // Detect format from data size to calculate correct RowPitch
            int expectedRGBA32 = width * height * 4;
            float bytesPerPixel = (float)size / (width * height);
            int rowPitch;

            if (bytesPerPixel < 1.0f) {
                // Block compressed format (BC7, BC1, etc.)
                // BC7/BC1/BC3 = 16 bytes per 4x4 block
                int blocksPerRow = (width + 3) / 4;
                rowPitch = blocksPerRow * 16;
            } else {
                // Uncompressed format (RGBA32)
                rowPitch = width * 4;
            }

            mipLevels.Add(new MipLevel {
                Level = miplevel,
                Width = width,
                Height = height,
                Data = mipData,
                RowPitch = rowPitch
            });

            return LibKtxNative.KtxErrorCode.KTX_SUCCESS;
        };

        DiagLog("[LoadFromHandle] Calling ktxTexture_IterateLevelFaces...");
        var iterResult = LibKtxNative.ktxTexture_IterateLevelFaces(textureHandle, callback, IntPtr.Zero);
        DiagLog($"[LoadFromHandle] IterateLevelFaces returned: {iterResult}, mipLevels.Count={mipLevels.Count}");

        if (iterResult != LibKtxNative.KtxErrorCode.KTX_SUCCESS) {
            throw new Exception($"Failed to iterate texture levels: {LibKtxNative.GetErrorString(iterResult)}");
        }

        if (mipLevels.Count == 0) {
            throw new Exception("No mip levels extracted from KTX2 texture");
        }
        DiagLog("[LoadFromHandle] Mip iteration complete");

        // Filter out mips with inconsistent format
        // BC7 blocks are 4x4, so for mips < 4x4 libktx returns uncompressed RGBA32
        // D3D11 texture must have uniform format across all mips

        // Detect primary format from first mip
        var firstMip = mipLevels[0];
        int mip0ExpectedRGBA = firstMip.Width * firstMip.Height * 4;
        float mip0BytesPerPixel = (float)firstMip.Data.Length / (firstMip.Width * firstMip.Height);
        bool primaryIsCompressed = mip0BytesPerPixel < 1.0f;

        // Filter mips to keep only those matching primary format
        int originalMipCount = mipLevels.Count;
        mipLevels = mipLevels.Where(mip => {
            float mipBytesPerPixel = (float)mip.Data.Length / (mip.Width * mip.Height);
            bool mipIsCompressed = mipBytesPerPixel < 1.0f;
            bool matches = mipIsCompressed == primaryIsCompressed;

            if (!matches) {
                Trace.WriteLine($"[KTX2LOADER] WARN: Discarding mip {mip.Level} ({mip.Width}x{mip.Height}): format mismatch (compressed={mipIsCompressed}, expected={primaryIsCompressed})");
            }

            return matches;
        }).ToList();

        if (mipLevels.Count < originalMipCount) {
            Trace.WriteLine($"[KTX2LOADER] Filtered mips: kept {mipLevels.Count} / {originalMipCount} (discarded {originalMipCount - mipLevels.Count} with mismatched format)");
        }

        // Use real dimensions from first mip level (iterator gives correct values)
        int actualWidth = mipLevels.Count > 0 ? mipLevels[0].Width : (int)tex.baseWidth;
        int actualHeight = mipLevels.Count > 0 ? mipLevels[0].Height : (int)tex.baseHeight;

        // Determine format: if we transcoded, we KNOW the format; otherwise detect from data
        string detectedFormat;
        bool isCompressed;
        bool hasAlpha = true;

        if (transcodeFormat != null) {
            // We transcoded - use the format we requested from libktx
            detectedFormat = transcodeFormat;
            isCompressed = true;
        } else {
            // No transcode - detect format from data size
            int mip0Actual = mipLevels[0].Data.Length;
            float bytesPerPixel = (float)mip0Actual / (actualWidth * actualHeight);

            isCompressed = bytesPerPixel < 2.0f; // Less than 2 bytes per pixel = compressed

            if (isCompressed) {
                // Compressed format - detect from bytesPerPixel
                string formatSuffix = isSRGB ? "_SRGB_BLOCK" : "_UNORM_BLOCK";

                if (Math.Abs(bytesPerPixel - 0.5f) < 0.1f) {
                    detectedFormat = "BC7" + formatSuffix;
                } else if (Math.Abs(bytesPerPixel - 1.0f) < 0.1f) {
                    detectedFormat = "BC3" + formatSuffix;
                } else {
                    detectedFormat = $"COMPRESSED_UNKNOWN({bytesPerPixel:F2} bytes/pixel)";
                    Trace.WriteLine($"[KTX2LOADER] WARN: Unknown compressed format: {bytesPerPixel:F2} bytes/pixel");
                }
            } else {
                // Uncompressed RGBA8
                detectedFormat = isSRGB ? "R8G8B8A8_SRGB" : "R8G8B8A8_UNORM";
            }
        }

        string sourceFormat = needsTranscode
            ? $"KTX2/BasisU -> {detectedFormat}"
            : $"KTX2 ({detectedFormat})";

        // Use Trace instead of NLog to avoid deadlock between UI and background threads
        System.Diagnostics.Trace.WriteLine($"[KTX2LOADER] KTX2 loaded: {actualWidth}x{actualHeight}, {mipLevels.Count} mips, {detectedFormat}");

        // Histogram metadata was already read before transcoding (passed as parameter)

        return new TextureData {
            Width = actualWidth,
            Height = actualHeight,
            MipLevels = mipLevels,
            IsSRGB = isSRGB,
            HasAlpha = hasAlpha,
            SourceFormat = sourceFormat,
            SourcePath = filePath, // Track KTX2 file path
            IsHDR = false,
            IsCompressed = isCompressed,
            CompressionFormat = isCompressed ? detectedFormat : null,
            HistogramMetadata = histogramMetadata,
            NormalLayoutMetadata = normalLayoutMetadata
        };
    }

    /// <summary>
    /// Load KTX2 via basisu CLI decoder (fallback when libktx transcode doesn't work).
    /// </summary>
    private static TextureData LoadViaBasisuCli(string filePath) {
        Trace.WriteLine($"[KTX2LOADER] Loading KTX2 via basisu CLI: {filePath}");

        // Create temp directory for unpacked PNGs
        string tempDir = Path.Combine(Path.GetTempPath(), "basisu_unpack_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try {
            // Find basisu.exe
            string? basisuExe = FindBasisuExecutable();
            if (basisuExe == null) {
                throw new Exception("basisu.exe not found. Please install Basis Universal or add basisu to PATH.");
            }

            // Copy KTX2 to temp dir (basisu outputs to same directory as input)
            string tempKtx = Path.Combine(tempDir, Path.GetFileName(filePath));
            File.Copy(filePath, tempKtx);

            // Run basisu -unpack
            var psi = new System.Diagnostics.ProcessStartInfo {
                FileName = basisuExe,
                Arguments = $"-unpack \"{tempKtx}\"",
                WorkingDirectory = tempDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) {
                throw new Exception("Failed to start basisu process");
            }

            // КРИТИЧНО: Читаем оба потока ПАРАЛЛЕЛЬНО чтобы избежать deadlock
            var stdoutBuilder = new System.Text.StringBuilder();
            var stderrBuilder = new System.Text.StringBuilder();

            process.OutputDataReceived += (sender, e) => {
                if (e.Data != null) stdoutBuilder.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (sender, e) => {
                if (e.Data != null) stderrBuilder.AppendLine(e.Data);
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            string stdout = stdoutBuilder.ToString();
            string stderr = stderrBuilder.ToString();

            if (process.ExitCode != 0) {
                Trace.WriteLine($"[KTX2LOADER] ERROR: basisu failed with exit code {process.ExitCode}");
                if (!string.IsNullOrEmpty(stderr)) Trace.WriteLine($"[KTX2LOADER] ERROR: stderr: {stderr}");
                throw new Exception($"basisu -unpack failed with exit code {process.ExitCode}");
            }

            // Find unpacked PNG files (basisu creates: filename_unpacked.png, filename_unpacked_mip1.png, etc.)
            string baseNameWithoutExt = Path.GetFileNameWithoutExtension(tempKtx);
            var pngFiles = Directory.GetFiles(tempDir, $"{baseNameWithoutExt}_unpacked*.png")
                .OrderBy(f => f)
                .ToList();

            if (pngFiles.Count == 0) {
                // Sometimes basisu outputs just "_unpacked.png" without original name
                pngFiles = Directory.GetFiles(tempDir, "*_unpacked*.png")
                    .OrderBy(f => f)
                    .ToList();
            }

            if (pngFiles.Count == 0) {
                throw new Exception($"No PNG files created by basisu in {tempDir}");
            }

            // Load PNG files as mips using ImageSharp
            var mipLevels = new List<MipLevel>();
            int width = 0, height = 0;

            for (int i = 0; i < pngFiles.Count; i++) {
                using var img = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(pngFiles[i]);

                if (i == 0) {
                    width = img.Width;
                    height = img.Height;
                }

                byte[] mipData = new byte[img.Width * img.Height * 4];
                img.ProcessPixelRows(accessor => {
                    for (int y = 0; y < img.Height; y++) {
                        var row = accessor.GetRowSpan(y);
                        int rowOffset = y * img.Width * 4;
                        for (int x = 0; x < img.Width; x++) {
                            var pixel = row[x];
                            int pixelOffset = rowOffset + x * 4;
                            mipData[pixelOffset] = pixel.R;
                            mipData[pixelOffset + 1] = pixel.G;
                            mipData[pixelOffset + 2] = pixel.B;
                            mipData[pixelOffset + 3] = pixel.A;
                        }
                    }
                });

                mipLevels.Add(new MipLevel {
                    Level = i,
                    Width = img.Width,
                    Height = img.Height,
                    Data = mipData,
                    RowPitch = img.Width * 4
                });
            }

            // Check for meaningful alpha (sample every 16th pixel)
            bool hasAlpha = false;
            var mip0Data = mipLevels[0].Data;
            for (int i = 3; i < mip0Data.Length; i += 64) { // Every 16th pixel (16 * 4 = 64)
                if (mip0Data[i] != 255) {
                    hasAlpha = true;
                    break;
                }
            }

            return new TextureData {
                Width = width,
                Height = height,
                MipLevels = mipLevels,
                IsSRGB = true, // Assume sRGB for Basis textures
                HasAlpha = hasAlpha,
                SourceFormat = "KTX2/BasisU (via basisu CLI)",
                IsHDR = false,
                IsCompressed = false, // Now uncompressed RGBA8
                CompressionFormat = null
            };
        } finally {
            // Clean up temp directory
            try {
                Directory.Delete(tempDir, true);
            } catch {
                // Ignore cleanup errors
            }
        }
    }

    /// <summary>
    /// Find basisu.exe executable.
    /// </summary>
    private static string? FindBasisuExecutable() {
        // Try common locations
        string[] searchPaths = {
            "basisu",
            "basisu.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links", "basisu.exe"),
            @"C:\Program Files\Basis Universal\basisu.exe",
            @"C:\tools\basisu\basisu.exe"
        };

        foreach (var path in searchPaths) {
            try {
                var psi = new System.Diagnostics.ProcessStartInfo {
                    FileName = path,
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(psi);
                if (process != null) {
                    // Читаем stdout/stderr ПЕРЕД WaitForExit чтобы избежать deadlock
                    process.StandardOutput.ReadToEnd();
                    process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode == 0 || process.ExitCode == 1) {
                        Trace.WriteLine($"[KTX2LOADER] Found basisu: {path}");
                        return path;
                    }
                }
            } catch (Exception ex) {
                Trace.WriteLine($"[KTX2LOADER] Failed to execute {path}: {ex.Message}");
                // Continue searching
            }
        }

        Trace.WriteLine("[KTX2LOADER] WARN: basisu.exe not found in any search path");
        return null;
    }
}
