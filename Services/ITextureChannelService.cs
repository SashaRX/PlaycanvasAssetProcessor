using System.Windows.Media.Imaging;

namespace AssetProcessor.Services;

public interface ITextureChannelService {
    Task<BitmapSource> ApplyChannelFilterAsync(BitmapSource source, string channel);
}
