using System.Windows;
using AssetProcessor.MasterMaterials.Models;
using AssetProcessor.ViewModels;

namespace AssetProcessor.Windows;

/// <summary>
/// Interaction logic for ChunkEditorWindow.xaml
/// </summary>
public partial class ChunkEditorWindow : Window
{
    /// <summary>
    /// The ViewModel for this window
    /// </summary>
    public ChunkEditorViewModel ViewModel { get; }

    /// <summary>
    /// The edited chunk (set when Save is clicked and validation passes)
    /// </summary>
    public ShaderChunk? EditedChunk { get; private set; }

    /// <summary>
    /// Creates a new ChunkEditorWindow for editing an existing chunk
    /// </summary>
    public ChunkEditorWindow(ShaderChunk chunk)
    {
        InitializeComponent();

        // Explicitly apply theme resources (workaround for DynamicResource not resolving)
        if (Application.Current.Resources["ThemeBackground"] is System.Windows.Media.Brush bgBrush)
            Background = bgBrush;

        ViewModel = new ChunkEditorViewModel();
        ViewModel.LoadChunk(chunk);
        DataContext = ViewModel;

        // Set initial focus to the GLSL editor
        Loaded += (_, _) => GlslEditor.Focus();
    }

    /// <summary>
    /// Creates a new ChunkEditorWindow for creating a new chunk
    /// </summary>
    public ChunkEditorWindow() : this(new ShaderChunk
    {
        Id = "newChunk",
        Type = "fragment",
        Description = "New shader chunk",
        Glsl = "// GLSL code here\n",
        Wgsl = "// WGSL code here\n"
    })
    {
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

        EditedChunk = ViewModel.ToChunk();
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
}
