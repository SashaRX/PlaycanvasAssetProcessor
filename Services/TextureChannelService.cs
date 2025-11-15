using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Advanced;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace AssetProcessor.Services;

public sealed class TextureChannelService : ITextureChannelService {
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
        using Image<Rgba32> image = SixLabors.ImageSharp.Image.Load<Rgba32>(BitmapSourceToArray(source));

        switch (channel) {
            case "R":
                ProcessChannel(image, pixel => new Rgba32(pixel.R, pixel.R, pixel.R, pixel.A));
                break;
            case "G":
                ProcessChannel(image, pixel => new Rgba32(pixel.G, pixel.G, pixel.G, pixel.A));
                break;
            case "B":
                ProcessChannel(image, pixel => new Rgba32(pixel.B, pixel.B, pixel.B, pixel.A));
                break;
            case "A":
                ProcessChannel(image, pixel => new Rgba32(pixel.A, pixel.A, pixel.A, pixel.A));
                break;
        }

        BitmapImage bitmapImage = BitmapToBitmapSource(image);
        if (!bitmapImage.IsFrozen) {
            bitmapImage.Freeze();
        }

        return bitmapImage;
    }

    private static byte[] BitmapSourceToArray(BitmapSource bitmapSource) {
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
        using MemoryStream stream = new();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static BitmapImage BitmapToBitmapSource(Image<Rgba32> image) {
        using MemoryStream memoryStream = new();
        image.SaveAsBmp(memoryStream);
        memoryStream.Seek(0, SeekOrigin.Begin);
        MemoryStream copyStream = new(memoryStream.ToArray());
        try {
            BitmapImage bitmapImage = new();
            bitmapImage.BeginInit();
            bitmapImage.StreamSource = copyStream;
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.EndInit();
            return bitmapImage;
        } finally {
            copyStream.Dispose();
        }
    }

    private static void ProcessChannel(Image<Rgba32> image, Func<Rgba32, Rgba32> transform) {
        int width = image.Width;
        int height = image.Height;
        int numberOfChunks = Environment.ProcessorCount;
        int chunkHeight = height / numberOfChunks;

        Parallel.For(0, numberOfChunks, chunk => {
            int startY = chunk * chunkHeight;
            int endY = chunk == numberOfChunks - 1 ? height : startY + chunkHeight;

            for (int y = startY; y < endY; y++) {
                Span<Rgba32> pixelRow = image.Frames.RootFrame.DangerousGetPixelRowMemory(y).Span;
                for (int x = 0; x < width; x++) {
                    pixelRow[x] = transform(pixelRow[x]);
                }
            }
        });
    }
}
