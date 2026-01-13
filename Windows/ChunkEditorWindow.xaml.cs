using System.Reflection;
using System.Windows;
using System.Xml;
using AssetProcessor.MasterMaterials.Models;
using AssetProcessor.ViewModels;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace AssetProcessor.Windows;

/// <summary>
/// Interaction logic for ChunkEditorWindow.xaml
/// </summary>
public partial class ChunkEditorWindow : Window
{
    private static IHighlightingDefinition? _glslHighlighting;
    private static IHighlightingDefinition? _wgslHighlighting;

    /// <summary>
    /// The ViewModel for this window
    /// </summary>
    public ChunkEditorViewModel ViewModel { get; }

    /// <summary>
    /// The edited chunk (set when Save is clicked and validation passes)
    /// </summary>
    public ShaderChunk? EditedChunk { get; private set; }

    /// <summary>
    /// Whether the editor is in read-only mode (for built-in chunks)
    /// </summary>
    public bool IsReadOnly { get; }

    /// <summary>
    /// Creates a new ChunkEditorWindow for editing an existing chunk
    /// </summary>
    public ChunkEditorWindow(ShaderChunk chunk, bool isReadOnly = false)
    {
        InitializeComponent();
        IsReadOnly = isReadOnly;

        // Explicitly apply theme resources (workaround for DynamicResource not resolving)
        if (Application.Current.Resources["ThemeBackground"] is System.Windows.Media.Brush bgBrush)
        {
            Background = bgBrush;
            RootGrid.Background = bgBrush;
            HeaderGrid.Background = bgBrush;
            FooterGrid.Background = bgBrush;
            CodeTabControl.Background = bgBrush;
        }

        // Initialize syntax highlighting
        InitializeSyntaxHighlighting();

        ViewModel = new ChunkEditorViewModel();
        ViewModel.LoadChunk(chunk);
        DataContext = ViewModel;

        // Set editor content
        GlslEditor.Text = ViewModel.GlslCode ?? "";
        WgslEditor.Text = ViewModel.WgslCode ?? "";

        // Wire up text change events to update ViewModel
        GlslEditor.TextChanged += (_, _) =>
        {
            if (ViewModel.GlslCode != GlslEditor.Text)
            {
                ViewModel.GlslCode = GlslEditor.Text;
            }
        };

        WgslEditor.TextChanged += (_, _) =>
        {
            if (ViewModel.WgslCode != WgslEditor.Text)
            {
                ViewModel.WgslCode = WgslEditor.Text;
            }
        };

        // Configure read-only mode for built-in chunks
        if (isReadOnly)
        {
            Title = $"View Chunk: {chunk.Id} (Built-in, Read-Only)";
            GlslEditor.IsReadOnly = true;
            WgslEditor.IsReadOnly = true;
            ChunkIdTextBox.IsReadOnly = true;
            DescriptionTextBox.IsReadOnly = true;
            TypeComboBox.IsEnabled = false;
            SaveButton.Content = "Copy to Edit";
            SaveButton.ToolTip = "Create an editable copy of this chunk";
            ResetButton.Visibility = Visibility.Collapsed;
        }

        // Set initial focus to the GLSL editor
        Loaded += (_, _) => GlslEditor.Focus();
    }

    /// <summary>
    /// Initializes syntax highlighting for GLSL and WGSL
    /// </summary>
    private void InitializeSyntaxHighlighting()
    {
        // Load GLSL highlighting
        if (_glslHighlighting == null)
        {
            _glslHighlighting = LoadHighlightingDefinition("AssetProcessor.SyntaxHighlighting.GLSL.xshd");
        }

        // Load WGSL highlighting
        if (_wgslHighlighting == null)
        {
            _wgslHighlighting = LoadHighlightingDefinition("AssetProcessor.SyntaxHighlighting.WGSL.xshd");
        }

        // Apply highlighting to editors
        if (_glslHighlighting != null)
        {
            GlslEditor.SyntaxHighlighting = _glslHighlighting;
        }

        if (_wgslHighlighting != null)
        {
            WgslEditor.SyntaxHighlighting = _wgslHighlighting;
        }
    }

    /// <summary>
    /// Loads a syntax highlighting definition from an embedded resource
    /// </summary>
    private static IHighlightingDefinition? LoadHighlightingDefinition(string resourceName)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(resourceName);

            if (stream == null)
            {
                System.Diagnostics.Debug.WriteLine($"Could not find embedded resource: {resourceName}");
                return null;
            }

            using var reader = new XmlTextReader(stream);
            return HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading syntax highlighting: {ex.Message}");
            return null;
        }
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
        // For read-only mode, return the original chunk for copying
        if (IsReadOnly)
        {
            EditedChunk = ViewModel.ToChunk();
            EditedChunk.IsBuiltIn = true; // Mark for copying
            DialogResult = true;
            Close();
            return;
        }

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
            // Update editor content after reset
            GlslEditor.Text = ViewModel.GlslCode ?? "";
            WgslEditor.Text = ViewModel.WgslCode ?? "";
        }
    }
}
