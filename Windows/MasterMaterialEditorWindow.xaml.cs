using System.Windows;
using System.Windows.Controls;
using AssetProcessor.MasterMaterials.Models;
using AssetProcessor.ViewModels;

namespace AssetProcessor.Windows;

/// <summary>
/// Interaction logic for MasterMaterialEditorWindow.xaml
/// </summary>
public partial class MasterMaterialEditorWindow : Window
{
    /// <summary>
    /// The ViewModel for this window
    /// </summary>
    public MasterMaterialEditorViewModel ViewModel { get; }

    /// <summary>
    /// The edited master material (set when Save is clicked and validation passes)
    /// </summary>
    public MasterMaterial? EditedMaster { get; private set; }

    /// <summary>
    /// Available chunks that can be added to the master
    /// </summary>
    private readonly IReadOnlyList<ShaderChunk> _availableChunks;

    /// <summary>
    /// Creates a new MasterMaterialEditorWindow for editing an existing master material
    /// </summary>
    public MasterMaterialEditorWindow(MasterMaterial master, IReadOnlyList<ShaderChunk> availableChunks)
    {
        InitializeComponent();

        // Explicitly apply theme resources (workaround for DynamicResource not resolving)
        if (Application.Current.Resources["ThemeBackground"] is System.Windows.Media.Brush bgBrush)
        {
            Background = bgBrush;
            RootGrid.Background = bgBrush;
            HeaderGrid.Background = bgBrush;
            FooterGrid.Background = bgBrush;
        }

        _availableChunks = availableChunks;
        ViewModel = new MasterMaterialEditorViewModel();
        ViewModel.LoadMaster(master);

        // Populate available chunks
        foreach (var chunk in availableChunks)
        {
            ViewModel.AvailableChunks.Add(chunk);
        }

        DataContext = ViewModel;

        // Set initial focus to the name textbox
        Loaded += (_, _) => NameTextBox.Focus();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var (isValid, errorMessage) = ViewModel.Validate();

        if (!isValid)
        {
            MessageBox.Show(
                errorMessage,
                "Validation Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        EditedMaster = ViewModel.ToMasterMaterial();
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.HasUnsavedChanges)
        {
            var result = MessageBox.Show(
                "You have unsaved changes. Are you sure you want to close?",
                "Unsaved Changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        DialogResult = false;
        Close();
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Reset all changes to original values?",
            "Reset Changes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            ViewModel.Reset();
        }
    }

    private void AddChunk_Click(object sender, RoutedEventArgs e)
    {
        // Get chunks that are not already attached
        var availableToAdd = _availableChunks
            .Where(c => !ViewModel.AttachedChunks.Any(x => x.ChunkName == c.Id))
            .ToList();

        if (availableToAdd.Count == 0)
        {
            MessageBox.Show(
                "All available chunks are already attached to this master material.",
                "No Chunks Available",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // Show a simple selection dialog
        var selectWindow = new Window
        {
            Title = "Select Chunk",
            Width = 300,
            Height = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = Application.Current.Resources["ThemeBackground"] as System.Windows.Media.Brush
        };

        var listBox = new ListBox
        {
            ItemsSource = availableToAdd,
            DisplayMemberPath = "Id",
            Margin = new Thickness(10),
            Background = Application.Current.Resources["ThemeBackgroundAlt"] as System.Windows.Media.Brush,
            Foreground = Application.Current.Resources["ThemeForeground"] as System.Windows.Media.Brush
        };

        var okButton = new Button
        {
            Content = "Add",
            Width = 80,
            Margin = new Thickness(10),
            HorizontalAlignment = HorizontalAlignment.Right
        };

        okButton.Click += (s, args) =>
        {
            if (listBox.SelectedItem is ShaderChunk selectedChunk)
            {
                ViewModel.AddChunk(selectedChunk.Id);
                selectWindow.Close();
            }
        };

        var panel = new DockPanel();
        DockPanel.SetDock(okButton, Dock.Bottom);
        panel.Children.Add(okButton);
        panel.Children.Add(listBox);

        selectWindow.Content = panel;
        selectWindow.ShowDialog();
    }

    private void RemoveChunk_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedChunk == null)
        {
            MessageBox.Show(
                "Please select a chunk to remove.",
                "No Selection",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        ViewModel.RemoveSelectedChunk();
    }
}
