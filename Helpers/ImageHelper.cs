using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Metadata;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace TexTool.Helpers {
    public static class ImageHelper {
        public static async Task<(int Width, int Height)> GetImageResolutionAsync(string url, CancellationToken cancellationToken) {
            if (string.IsNullOrEmpty(url)) {
                throw new ArgumentException("URL cannot be null or empty", nameof(url));
            }

            try {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Settings.Default.PlaycanvasApiKey);
                client.DefaultRequestHeaders.Range = new RangeHeaderValue(0, 24);

                var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                var buffer = await response.Content.ReadAsByteArrayAsync(cancellationToken);

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
                    using var stream = new MemoryStream(buffer);
                    using var image = System.Drawing.Image.FromStream(stream);
                    return (image.Width, image.Height);
                }

                throw new Exception("Image format not supported or not a PNG/JPEG/BMP/GIF/TIFF");
            } catch (HttpRequestException e) {
                throw new Exception("HttpRequestException: " + e.Message);
            } catch (Exception ex) {
                throw new Exception("Exception: " + ex.Message);
            }
        }

        private static async Task<(int Width, int Height)> GetJpegResolutionFromStream(string url, CancellationToken cancellationToken) {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Settings.Default.PlaycanvasApiKey);
            client.DefaultRequestHeaders.Range = new RangeHeaderValue(0, 500);

            var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var buffer = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            if (buffer.Length < 24) {
                throw new Exception("Unable to read image header");
            }

            using var stream = new MemoryStream(buffer);
            using var image = SixLabors.ImageSharp.Image.Load(stream);
            return (image.Width, image.Height);
        }
    }
}
