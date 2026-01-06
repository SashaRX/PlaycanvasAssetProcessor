using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AssetProcessor.Services;

public sealed class TextureChannelService : ITextureChannelService {
    private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

    public Task<BitmapSource> ApplyChannelFilterAsync(BitmapSource source, string channel) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(channel);

        BitmapSource frozenSource = source.Clone();
        if (frozenSource is Freezable freezable && !freezable.IsFrozen) {
            freezable.Freeze();
        }

        return Task.Run(() => ApplyChannelFilterInternal(frozenSource, channel));
    }

    private static BitmapSource ApplyChannelFilterInternal(BitmapSource source, string channel) {
        BitmapSource normalized = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        int width = normalized.PixelWidth;
        int height = normalized.PixelHeight;
        int bytesPerPixel = normalized.Format.BitsPerPixel / 8;
        int stride = width * bytesPerPixel;

        byte[] pixels = new byte[stride * height];
        normalized.CopyPixels(pixels, stride, 0);

        // Log sample pixel values before filtering
        int sampleIdx = (height / 2) * stride + (width / 2) * bytesPerPixel;
        byte sampleB = pixels[sampleIdx];
        byte sampleG = pixels[sampleIdx + 1];
        byte sampleR = pixels[sampleIdx + 2];
        byte sampleA = pixels[sampleIdx + 3];
        logger.Info($"[ChannelFilter] Input channel={channel}, center pixel RGBA=({sampleR},{sampleG},{sampleB},{sampleA})");

        byte[] output = new byte[pixels.Length];
        Buffer.BlockCopy(pixels, 0, output, 0, pixels.Length);

        for (int y = 0; y < height; y++) {
            int rowStart = y * stride;
            for (int x = 0; x < width; x++) {
                int index = rowStart + x * bytesPerPixel;
                byte b = pixels[index];
                byte g = pixels[index + 1];
                byte r = pixels[index + 2];
                byte a = pixels[index + 3];

                byte channelValue = channel switch {
                    "R" => r,
                    "G" => g,
                    "B" => b,
                    "A" => a,
                    _ => 0
                };

                output[index] = channelValue;
                output[index + 1] = channelValue;
                output[index + 2] = channelValue;

                output[index + 3] = channel == "A" ? channelValue : a;
            }
        }

        // Log sample pixel value after filtering
        byte filteredValue = output[sampleIdx]; // All RGB are same after filtering
        logger.Info($"[ChannelFilter] Output channel={channel}, center pixel value={filteredValue}");

        WriteableBitmap result = new(width, height, normalized.DpiX, normalized.DpiY, PixelFormats.Bgra32, null);
        result.WritePixels(new Int32Rect(0, 0, width, height), output, stride, 0);
        if (!result.IsFrozen) {
            result.Freeze();
        }

        return result;
    }
}
