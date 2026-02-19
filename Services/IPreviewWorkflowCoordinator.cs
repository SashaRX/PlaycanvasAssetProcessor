using System.ComponentModel;

namespace AssetProcessor.Services;

public interface IPreviewWorkflowCoordinator {
    void ApplyTextureGrouping(ICollectionView view, bool groupByType);
}
