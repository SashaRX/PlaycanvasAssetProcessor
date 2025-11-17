using AssetProcessor.Services.Models;
using System.Threading.Tasks;

namespace AssetProcessor.Services;

public interface IPreviewRendererCoordinator {
    Task SwitchRendererAsync(TexturePreviewContext context, bool useD3D11);
}
