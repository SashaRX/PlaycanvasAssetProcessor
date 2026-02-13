namespace AssetProcessor.Services {
    /// <summary>
    /// Groups PlayCanvas connection-related services to reduce MainWindow constructor parameters.
    /// Contains: API client, connection state, credentials, project selection,
    /// project connection protocol, and file watcher.
    /// </summary>
    public class ConnectionServiceFacade {
        public IPlayCanvasService PlayCanvasService { get; }
        public IConnectionStateService ConnectionStateService { get; }
        public IPlayCanvasCredentialsService CredentialsService { get; }
        public IProjectSelectionService ProjectSelectionService { get; }
        public IProjectConnectionService ProjectConnectionService { get; }
        public IProjectFileWatcherService ProjectFileWatcherService { get; }

        public ConnectionServiceFacade(
            IPlayCanvasService playCanvasService,
            IConnectionStateService connectionStateService,
            IPlayCanvasCredentialsService credentialsService,
            IProjectSelectionService projectSelectionService,
            IProjectConnectionService projectConnectionService,
            IProjectFileWatcherService projectFileWatcherService) {
            PlayCanvasService = playCanvasService;
            ConnectionStateService = connectionStateService;
            CredentialsService = credentialsService;
            ProjectSelectionService = projectSelectionService;
            ProjectConnectionService = projectConnectionService;
            ProjectFileWatcherService = projectFileWatcherService;
        }
    }
}
