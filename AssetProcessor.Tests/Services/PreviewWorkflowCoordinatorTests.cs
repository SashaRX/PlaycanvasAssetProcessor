using AssetProcessor.Services;
using System.Collections.ObjectModel;
using System.Windows.Data;

namespace AssetProcessor.Tests.Services;

public class PreviewWorkflowCoordinatorTests {
    [Fact]
    public void ApplyTextureGrouping_AddsGroupDescriptions_WhenEnabled() {
        var sut = new PreviewWorkflowCoordinator();
        var data = new ObservableCollection<object>();
        var view = CollectionViewSource.GetDefaultView(data);

        sut.ApplyTextureGrouping(view, true);

        Assert.Equal(2, view.GroupDescriptions.Count);
    }
}
