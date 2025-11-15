using AssetProcessor.Services;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace AssetProcessor.Tests.Services;

public class TextureChannelServiceTests {
    private readonly TextureChannelService service = new();

    [Theory]
    [InlineData("R", 10)]
    [InlineData("G", 20)]
    [InlineData("B", 30)]
    [InlineData("A", 40)]
    public async Task ApplyChannelFilterAsync_ReturnsExpectedGrayscale(string channel, byte expectedValue) {
        WriteableBitmap bitmap = new(1, 1, 96, 96, PixelFormats.Bgra32, null);
        byte[] pixel = [30, 20, 10, 40]; // B, G, R, A
        bitmap.WritePixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);

        BitmapSource result = await service.ApplyChannelFilterAsync(bitmap, channel);

        byte[] output = new byte[4];
        result.CopyPixels(output, 4, 0);

        Assert.Equal(expectedValue, output[0]);
        Assert.Equal(expectedValue, output[1]);
        Assert.Equal(expectedValue, output[2]);
        Assert.Equal(channel == "A" ? expectedValue : (byte)40, output[3]);
    }
}
