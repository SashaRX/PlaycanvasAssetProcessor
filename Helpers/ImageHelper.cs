using AssetProcessor.Settings;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;

namespace AssetProcessor.Helpers {
    public static class ImageHelper {
        public static async Task<(int Width, int Height)> GetImageResolutionAsync(string url, CancellationToken cancellationToken) {
            if (string.IsNullOrEmpty(url)) {
                throw new ArgumentException("URL cannot be null or empty", nameof(url));
            }

            try {
                using HttpClient client = new();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AppSettings.Default.PlaycanvasApiKey);
                client.DefaultRequestHeaders.Range = new RangeHeaderValue(0, 24);

                HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                byte[] buffer = await response.Content.ReadAsByteArrayAsync(cancellationToken);

                if (buffer.Length < 24) {
                    throw new Exception("Unable to read image header");
                }

                // PNG format
                if (buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47 &&
                    buffer[4] == 0x0D && buffer[5] == 0x0A && buffer[6] == 0x1A && buffer[7] == 0x0A) {
                    int width = BitConverter.ToInt32([buffer[19], buffer[18], buffer[17], buffer[16]], 0);
                    int height = BitConverter.ToInt32([buffer[23], buffer[22], buffer[21], buffer[20]], 0);
                    return (width, height);
                }
                // JPEG format
                else if (buffer[0] == 0xFF && buffer[1] == 0xD8) {
                    return await GetJpegResolutionFromStream(url, cancellationToken);
                }
                // BMP format
                else if (buffer[0] == 0x42 && buffer[1] == 0x4D) {
                    int width = BitConverter.ToInt32(buffer, 18);
                    int height = BitConverter.ToInt32(buffer, 22);
                    return (width, height);
                }
                // GIF format
                else if (buffer[0] == 0x47 && buffer[1] == 0x49 && buffer[2] == 0x46) {
                    int width = BitConverter.ToInt16(buffer, 6);
                    int height = BitConverter.ToInt16(buffer, 8);
                    return (width, height);
                }
                // TIFF format
                else if ((buffer[0] == 0x49 && buffer[1] == 0x49 && buffer[2] == 0x2A && buffer[3] == 0x00) ||
                         (buffer[0] == 0x4D && buffer[1] == 0x4D && buffer[2] == 0x00 && buffer[3] == 0x2A)) {
                    return await GetTiffResolutionAsync(url, cancellationToken);
                }

                throw new Exception("Image format not supported or not a PNG/JPEG/BMP/GIF/TIFF");
            } catch (HttpRequestException e) {
                throw new Exception("HttpRequestException: " + e.Message);
            } catch (Exception ex) {
                throw new Exception("Exception: " + ex.Message);
            }
        }

        private static async Task<(int Width, int Height)> GetJpegResolutionFromStream(string url, CancellationToken cancellationToken) {
            using HttpClient client = new();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AppSettings.Default.PlaycanvasApiKey);
            client.DefaultRequestHeaders.Range = new RangeHeaderValue(0, 500);

            HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            byte[] buffer = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            if (buffer.Length < 24) {
                throw new Exception("Unable to read image header");
            }

            using MemoryStream stream = new(buffer);
            using SixLabors.ImageSharp.Image image = SixLabors.ImageSharp.Image.Load(stream);
            return (image.Width, image.Height);
        }

        private static async Task<(int Width, int Height)> GetTiffResolutionAsync(string url, CancellationToken cancellationToken) {
            using HttpClient client = new();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AppSettings.Default.PlaycanvasApiKey);
            client.DefaultRequestHeaders.Range = new RangeHeaderValue(0, 2047);

            HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            byte[] buffer = await ReadFullyAsync(responseStream, cancellationToken);

            if (buffer.Length < 8) {
                throw new Exception("Unable to read TIFF header");
            }

            bool isLittleEndian = buffer[0] == 0x49 && buffer[1] == 0x49;
            uint ifdOffset = ReadUInt32(buffer, 4, isLittleEndian);

            if (ifdOffset + 2 >= buffer.Length) {
                throw new Exception("Invalid TIFF IFD offset");
            }

            ushort entryCount = ReadUInt16(buffer, (int)ifdOffset, isLittleEndian);
            int entriesStart = (int)ifdOffset + 2;

            const ushort ImageWidthTag = 256;
            const ushort ImageLengthTag = 257;

            int? width = null;
            int? height = null;

            for (int i = 0; i < entryCount; i++) {
                int entryOffset = entriesStart + (i * 12);
                if (entryOffset + 12 > buffer.Length) {
                    break;
                }

                ushort tag = ReadUInt16(buffer, entryOffset, isLittleEndian);
                ushort type = ReadUInt16(buffer, entryOffset + 2, isLittleEndian);
                uint count = ReadUInt32(buffer, entryOffset + 4, isLittleEndian);
                uint valueOffset = ReadUInt32(buffer, entryOffset + 8, isLittleEndian);

                int value = type switch {
                    3 when count >= 1 => ReadUInt16Value(buffer, entryOffset + 8, isLittleEndian),
                    4 when count >= 1 => (int)valueOffset,
                    _ => -1,
                };

                if (value < 0) {
                    continue;
                }

                if (tag == ImageWidthTag) {
                    width = value;
                } else if (tag == ImageLengthTag) {
                    height = value;
                }

                if (width.HasValue && height.HasValue) {
                    return (width.Value, height.Value);
                }
            }

            throw new Exception("Unable to determine TIFF image dimensions");
        }

        private static async Task<byte[]> ReadFullyAsync(Stream stream, CancellationToken cancellationToken) {
            using MemoryStream memoryStream = new();
            await stream.CopyToAsync(memoryStream, cancellationToken);
            return memoryStream.ToArray();
        }

        private static ushort ReadUInt16(byte[] buffer, int offset, bool littleEndian) {
            if (littleEndian) {
                return BitConverter.ToUInt16(buffer, offset);
            }

            return (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
        }

        private static uint ReadUInt32(byte[] buffer, int offset, bool littleEndian) {
            if (littleEndian) {
                return BitConverter.ToUInt32(buffer, offset);
            }

            return ((uint)buffer[offset] << 24) | ((uint)buffer[offset + 1] << 16) | ((uint)buffer[offset + 2] << 8) | buffer[offset + 3];
        }

        private static int ReadUInt16Value(byte[] buffer, int offset, bool littleEndian) {
            if (littleEndian) {
                return BitConverter.ToUInt16(buffer, offset);
            }

            return (buffer[offset] << 8) | buffer[offset + 1];
        }
    }
}
