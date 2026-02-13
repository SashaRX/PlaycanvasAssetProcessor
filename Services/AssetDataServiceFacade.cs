namespace AssetProcessor.Services {
    /// <summary>
    /// Groups asset data loading and management services to reduce MainWindow constructor parameters.
    /// Contains: asset loading coordinator, resource service, JSON parser,
    /// file status scanner, local cache, and project asset service.
    /// </summary>
    public class AssetDataServiceFacade {
        public IAssetLoadCoordinator AssetLoadCoordinator { get; }
        public IAssetResourceService AssetResourceService { get; }
        public IAssetJsonParserService AssetJsonParserService { get; }
        public IFileStatusScannerService FileStatusScannerService { get; }
        public ILocalCacheService LocalCacheService { get; }
        public IProjectAssetService ProjectAssetService { get; }

        public AssetDataServiceFacade(
            IAssetLoadCoordinator assetLoadCoordinator,
            IAssetResourceService assetResourceService,
            IAssetJsonParserService assetJsonParserService,
            IFileStatusScannerService fileStatusScannerService,
            ILocalCacheService localCacheService,
            IProjectAssetService projectAssetService) {
            AssetLoadCoordinator = assetLoadCoordinator;
            AssetResourceService = assetResourceService;
            AssetJsonParserService = assetJsonParserService;
            FileStatusScannerService = fileStatusScannerService;
            LocalCacheService = localCacheService;
            ProjectAssetService = projectAssetService;
        }
    }
}
