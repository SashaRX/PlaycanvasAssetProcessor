using AssetProcessor.Services.Models;
using System;
using System.Threading.Tasks;

namespace AssetProcessor.Services;

public class PreviewRendererCoordinator : IPreviewRendererCoordinator {
    private readonly ITexturePreviewService texturePreviewService;
    private readonly ILogService logService;

    public PreviewRendererCoordinator(ITexturePreviewService texturePreviewService, ILogService logService) {
        this.texturePreviewService = texturePreviewService ?? throw new ArgumentNullException(nameof(texturePreviewService));
        this.logService = logService ?? throw new ArgumentNullException(nameof(logService));
    }

    public Task SwitchRendererAsync(TexturePreviewContext context, bool useD3D11) {
        ArgumentNullException.ThrowIfNull(context);
        logService.LogInfo($"Switching preview renderer. Using D3D11: {useD3D11}");
        return texturePreviewService.SwitchRendererAsync(context, useD3D11);
    }
}
