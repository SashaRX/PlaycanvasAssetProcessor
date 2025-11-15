using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.IO;
using System.Windows.Media.Imaging;

namespace AssetProcessor.Services;

public sealed class TextureChannelService : ITextureChannelService {
    public async Task<BitmapSource> ApplyChannelFilterAsync(BitmapSource source, string channel) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(channel);

        using Image<Rgba32> image = SixLabors.ImageSharp.Image.Load<Rgba32>(BitmapSourceToArray(source));

        await Task.Run(() => {
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
        }).ConfigureAwait(false);

        return BitmapToBitmapSource(image);
    }

    private static byte[] BitmapSourceToArray(BitmapSource bitmapSource) {
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create((BitmapSource)bitmapSource.Clone()));
        using MemoryStream stream = new();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static BitmapImage BitmapToBitmapSource(Image<Rgba32> image) {
        using MemoryStream memoryStream = new();
        image.SaveAsBmp(memoryStream);
        memoryStream.Seek(0, SeekOrigin.Begin);
        BitmapImage bitmapImage = new();
        bitmapImage.BeginInit();
        bitmapImage.StreamSource = memoryStream;
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.EndInit();
        return bitmapImage;
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
                Span<Rgba32> pixelRow = image.Frames.RootFrame.GetPixelRowSpan(y);
                for (int x = 0; x < width; x++) {
                    pixelRow[x] = transform(pixelRow[x]);
                }
            }
        });
    }
}
