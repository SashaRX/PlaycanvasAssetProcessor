namespace AssetProcessor.Services {
    /// <summary>
    /// Groups texture preview and visualization services to reduce MainWindow constructor parameters.
    /// Contains: texture preview, channel filtering, preview renderer coordination,
    /// histogram analysis, ORM texture service, and KTX2 info service.
    /// </summary>
    public class TextureViewerServiceFacade {
        public ITexturePreviewService TexturePreviewService { get; }
        public ITextureChannelService TextureChannelService { get; }
        public IPreviewRendererCoordinator PreviewRendererCoordinator { get; }
        public IHistogramCoordinator HistogramCoordinator { get; }
        public IORMTextureService ORMTextureService { get; }
        public IKtx2InfoService Ktx2InfoService { get; }

        public TextureViewerServiceFacade(
            ITexturePreviewService texturePreviewService,
            ITextureChannelService textureChannelService,
            IPreviewRendererCoordinator previewRendererCoordinator,
            IHistogramCoordinator histogramCoordinator,
            IORMTextureService ormTextureService,
            IKtx2InfoService ktx2InfoService) {
            TexturePreviewService = texturePreviewService;
            TextureChannelService = textureChannelService;
            PreviewRendererCoordinator = previewRendererCoordinator;
            HistogramCoordinator = histogramCoordinator;
            ORMTextureService = ormTextureService;
            Ktx2InfoService = ktx2InfoService;
        }
    }
}
