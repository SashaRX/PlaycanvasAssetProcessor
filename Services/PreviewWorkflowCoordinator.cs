using System.ComponentModel;
using System.Windows.Data;

namespace AssetProcessor.Services;

public sealed class PreviewWorkflowCoordinator : IPreviewWorkflowCoordinator {
    public void ApplyTextureGrouping(ICollectionView view, bool groupByType) {
        if (!view.CanGroup) return;

        if (groupByType) {
            using (view.DeferRefresh()) {
                view.GroupDescriptions.Clear();
                view.GroupDescriptions.Add(new PropertyGroupDescription("GroupName"));
                view.GroupDescriptions.Add(new PropertyGroupDescription("SubGroupName"));
            }
            return;
        }

        if (view.GroupDescriptions.Count > 0) {
            view.GroupDescriptions.Clear();
        }
    }
}
