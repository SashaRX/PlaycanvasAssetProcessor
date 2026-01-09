# MVVM Refactoring Plan for MainWindow.xaml.cs

## Current State

- **MainWindow.xaml.cs**: 4,946 lines (monolithic code-behind)
- **MainWindow.Api.cs**: ~520 lines (partial class for asset loading)
- **Business logic in code-behind**: ~1,900 lines (38%)
- **Services injected directly into MainWindow**: 16+

## Target Architecture

```
MainWindow.xaml.cs (UI only)
    │
    ├── MainViewModel (orchestration)
    │       ├── TextureSelectionViewModel
    │       ├── MaterialSelectionViewModel
    │       ├── ORMTextureViewModel
    │       ├── TextureConversionSettingsViewModel
    │       └── AssetLoadingViewModel
    │
    └── Services Layer (existing)
```

---

## Phase 1: TextureSelectionViewModel

**Purpose**: Handle texture selection and preview loading logic

### Move from MainWindow:
- `TexturesDataGrid_SelectionChanged()` - 294 lines
- `TryLoadKtx2PreviewAsync()` / `TryLoadKtx2ToD3D11Async()` / `LoadSourcePreviewAsync()`
- `RefreshCurrentTexture()`

### New ViewModel:

```csharp
// ViewModels/TextureSelectionViewModel.cs
public partial class TextureSelectionViewModel : ObservableObject {
    private readonly ITexturePreviewService previewService;
    private readonly IResourceSettingsService settingsService;

    [ObservableProperty]
    private TextureResource? selectedTexture;

    [ObservableProperty]
    private bool isPreviewLoading;

    [ObservableProperty]
    private BitmapSource? currentPreview;

    public event EventHandler<TextureSelectedEventArgs>? TextureSelected;
    public event EventHandler<PreviewLoadedEventArgs>? PreviewLoaded;

    [RelayCommand]
    private async Task SelectTextureAsync(TextureResource? texture, CancellationToken ct) {
        if (texture == null) return;

        SelectedTexture = texture;
        IsPreviewLoading = true;

        try {
            // Load preview based on texture type
            if (texture.IsORMTexture) {
                CurrentPreview = await previewService.LoadORMPreviewAsync(texture, ct);
            } else if (File.Exists(Path.ChangeExtension(texture.Path, ".ktx2"))) {
                CurrentPreview = await previewService.LoadKtx2PreviewAsync(texture, ct);
            } else {
                CurrentPreview = await previewService.LoadSourcePreviewAsync(texture, ct);
            }

            PreviewLoaded?.Invoke(this, new PreviewLoadedEventArgs(texture, CurrentPreview));
        } finally {
            IsPreviewLoading = false;
        }

        TextureSelected?.Invoke(this, new TextureSelectedEventArgs(texture));
    }

    [RelayCommand]
    private async Task RefreshPreviewAsync(CancellationToken ct) {
        if (SelectedTexture == null) return;
        await SelectTextureAsync(SelectedTexture, ct);
    }
}
```

### XAML Changes:

```xml
<!-- Before -->
<DataGrid SelectionChanged="TexturesDataGrid_SelectionChanged" />

<!-- After -->
<DataGrid SelectedItem="{Binding TextureSelectionVM.SelectedTexture, Mode=TwoWay}">
    <i:Interaction.Triggers>
        <i:EventTrigger EventName="SelectionChanged">
            <i:InvokeCommandAction
                Command="{Binding TextureSelectionVM.SelectTextureCommand}"
                CommandParameter="{Binding SelectedItem, RelativeSource={RelativeSource AncestorType=DataGrid}}" />
        </i:EventTrigger>
    </i:Interaction.Triggers>
</DataGrid>
```

---

## Phase 2: ORMTextureViewModel

**Purpose**: Dedicated ORM texture creation, management, and operations

### Move from MainWindow:
- `GenerateVirtualORMTextures()` - 106 lines
- `DetectAndLoadORMTextures()` - 92 lines
- `CreateORMButton_Click()` - 16 lines
- `CreateORMFromMaterial_Click()` - 140 lines
- `CreateORMForAllMaterials_Click()` - 94 lines
- `DeleteORMTexture_Click()` / `DeleteORMFromList_Click()` - 50 lines
- `LoadORMPreviewAsync()` - 97 lines

### New ViewModel:

```csharp
// ViewModels/ORMTextureViewModel.cs
public partial class ORMTextureViewModel : ObservableObject {
    private readonly IORMTextureService ormService;
    private readonly ITexturePreviewService previewService;

    [ObservableProperty]
    private ObservableCollection<ORMTextureResource> virtualORMTextures = [];

    [ObservableProperty]
    private bool isCreatingORM;

    [ObservableProperty]
    private int creationProgress;

    [ObservableProperty]
    private int creationTotal;

    public event EventHandler<ORMCreatedEventArgs>? ORMCreated;

    [RelayCommand]
    private async Task CreateORMFromMaterialAsync(MaterialResource material, CancellationToken ct) {
        if (material == null) return;

        IsCreatingORM = true;
        try {
            var result = await ormService.CreateORMFromMaterialAsync(material, ct);
            if (result.Success) {
                VirtualORMTextures.Add(result.ORMTexture);
                ORMCreated?.Invoke(this, new ORMCreatedEventArgs(result.ORMTexture));
            }
        } finally {
            IsCreatingORM = false;
        }
    }

    [RelayCommand]
    private async Task CreateAllORMsAsync(
        IEnumerable<MaterialResource> materials,
        CancellationToken ct) {

        var materialList = materials.ToList();
        CreationTotal = materialList.Count;
        CreationProgress = 0;
        IsCreatingORM = true;

        try {
            foreach (var material in materialList) {
                ct.ThrowIfCancellationRequested();
                await CreateORMFromMaterialAsync(material, ct);
                CreationProgress++;
            }
        } finally {
            IsCreatingORM = false;
        }
    }

    [RelayCommand]
    private async Task DeleteORMAsync(ORMTextureResource orm, CancellationToken ct) {
        if (orm == null) return;

        await ormService.DeleteORMAsync(orm, ct);
        VirtualORMTextures.Remove(orm);
    }

    /// <summary>
    /// Detects existing ORM textures from KTX2 files on disk
    /// </summary>
    public async Task DetectAndLoadORMTexturesAsync(
        string projectPath,
        IEnumerable<TextureResource> textures,
        CancellationToken ct) {

        var detected = await ormService.DetectORMTexturesAsync(projectPath, textures, ct);

        foreach (var orm in detected) {
            VirtualORMTextures.Add(orm);
        }
    }

    /// <summary>
    /// Generates virtual ORM textures for texture groups
    /// </summary>
    public void GenerateVirtualORMTextures(
        IEnumerable<TextureResource> textures,
        int projectId) {

        var generated = ormService.GenerateVirtualORMTextures(textures, projectId);

        foreach (var orm in generated) {
            if (!VirtualORMTextures.Any(o => o.SettingsKey == orm.SettingsKey)) {
                VirtualORMTextures.Add(orm);
            }
        }
    }
}
```

### XAML Changes:

```xml
<!-- Before -->
<Button Click="CreateORMFromMaterial_Click" Content="Create ORM" />

<!-- After -->
<Button Command="{Binding ORMTextureVM.CreateORMFromMaterialCommand}"
        CommandParameter="{Binding SelectedMaterial}"
        Content="Create ORM" />
```

---

## Phase 3: TextureConversionSettingsViewModel

**Purpose**: Handle texture-specific conversion settings persistence and loading

### Move from MainWindow:
- `LoadTextureConversionSettings()` - 54 lines
- `InitializeTextureConversionSettings()` - 93 lines
- `SaveTextureSettingsToService()` - 60 lines
- `LoadSavedSettingsToUI()` - 81 lines
- `UpdateTextureConversionSettings()` - 16 lines

### New ViewModel:

```csharp
// ViewModels/TextureConversionSettingsViewModel.cs
public partial class TextureConversionSettingsViewModel : ObservableObject {
    private readonly IResourceSettingsService settingsService;
    private readonly IPresetManager presetManager;

    [ObservableProperty]
    private TextureResource? currentTexture;

    [ObservableProperty]
    private bool isLoadingSettings;

    [ObservableProperty]
    private CompressionSettings? currentSettings;

    [ObservableProperty]
    private string? detectedPresetName;

    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

    [RelayCommand]
    private async Task LoadSettingsForTextureAsync(TextureResource texture, CancellationToken ct) {
        if (texture == null) return;

        CurrentTexture = texture;
        IsLoadingSettings = true;

        try {
            // Try to load saved settings first
            var saved = settingsService.GetTextureSettings(texture.ProjectId, texture.SettingsKey);
            if (saved != null) {
                CurrentSettings = saved;
                return;
            }

            // Auto-detect preset based on filename
            var preset = presetManager.FindPresetByFileName(texture.Name);
            if (preset != null) {
                DetectedPresetName = preset.Name;
                CurrentSettings = preset.Settings;
            }
        } finally {
            IsLoadingSettings = false;
        }
    }

    [RelayCommand]
    private void SaveSettings() {
        if (CurrentTexture == null || CurrentSettings == null) return;

        settingsService.SaveTextureSettings(
            CurrentTexture.ProjectId,
            CurrentTexture.SettingsKey,
            CurrentSettings);

        SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(CurrentTexture, CurrentSettings));
    }
}
```

---

## Phase 4: AssetLoadingViewModel

**Purpose**: Orchestrate asset loading from JSON, ORM detection, virtual texture generation

### Move from MainWindow.Api.cs:
- `LoadAssetsFromJsonFileAsync()` - 69 lines
- `PostProcessLoadedAssetsAsync()` - 18 lines
- `RestoreUploadStatesAsync()` - 40 lines
- `ScanKtx2InfoForAllTextures()` - 80 lines

### New ViewModel:

```csharp
// ViewModels/AssetLoadingViewModel.cs
public partial class AssetLoadingViewModel : ObservableObject {
    private readonly IAssetLoadingService assetService;
    private readonly IUploadStateService uploadStateService;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private int loadingProgress;

    [ObservableProperty]
    private int loadingTotal;

    [ObservableProperty]
    private string? loadingStatus;

    public event EventHandler<AssetsLoadedEventArgs>? AssetsLoaded;

    [RelayCommand]
    private async Task LoadAssetsAsync(AssetLoadRequest request, CancellationToken ct) {
        IsLoading = true;
        LoadingStatus = "Loading assets...";

        try {
            var progress = new Progress<AssetLoadProgress>(p => {
                LoadingProgress = p.Processed;
                LoadingTotal = p.Total;
            });

            var result = await assetService.LoadAssetsFromJsonAsync(
                request.ProjectPath,
                request.ProjectName,
                progress,
                ct);

            AssetsLoaded?.Invoke(this, new AssetsLoadedEventArgs(result));
        } finally {
            IsLoading = false;
        }
    }
}
```

---

## Phase 5: MaterialSelectionViewModel

**Purpose**: Handle material selection and texture filtering

### Move from MainWindow:
- `MaterialsDataGrid_SelectionChanged()` - 22 lines
- `DisplayMaterialParameters()` - scattered logic
- Material texture ID extraction

### New ViewModel:

```csharp
// ViewModels/MaterialSelectionViewModel.cs
public partial class MaterialSelectionViewModel : ObservableObject {
    [ObservableProperty]
    private MaterialResource? selectedMaterial;

    [ObservableProperty]
    private ObservableCollection<TextureResource> materialTextures = [];

    public event EventHandler<MaterialSelectedEventArgs>? MaterialSelected;

    partial void OnSelectedMaterialChanged(MaterialResource? value) {
        UpdateMaterialTextures(value);
        MaterialSelected?.Invoke(this, new MaterialSelectedEventArgs(value));
    }

    private void UpdateMaterialTextures(MaterialResource? material) {
        MaterialTextures.Clear();
        if (material == null) return;

        // Collect texture IDs from material
        var textureIds = new List<int>();
        if (material.DiffuseMapId.HasValue) textureIds.Add(material.DiffuseMapId.Value);
        if (material.NormalMapId.HasValue) textureIds.Add(material.NormalMapId.Value);
        // ... etc

        // Filter from main texture collection
        foreach (var id in textureIds) {
            var texture = AllTextures.FirstOrDefault(t => t.ID == id);
            if (texture != null) MaterialTextures.Add(texture);
        }
    }

    [RelayCommand]
    private void NavigateToTexture(int textureId) {
        var texture = AllTextures.FirstOrDefault(t => t.ID == textureId);
        if (texture != null) {
            // Raise event to navigate in UI
            NavigateToTextureRequested?.Invoke(this, new NavigateToTextureEventArgs(texture));
        }
    }

    public event EventHandler<NavigateToTextureEventArgs>? NavigateToTextureRequested;
    public IEnumerable<TextureResource> AllTextures { get; set; } = [];
}
```

---

## Updated MainViewModel

After refactoring, MainViewModel becomes the orchestrator:

```csharp
public partial class MainViewModel : ObservableObject {
    // Child ViewModels
    public TextureSelectionViewModel TextureSelectionVM { get; }
    public MaterialSelectionViewModel MaterialSelectionVM { get; }
    public ORMTextureViewModel ORMTextureVM { get; }
    public TextureConversionSettingsViewModel ConversionSettingsVM { get; }
    public AssetLoadingViewModel AssetLoadingVM { get; }

    // Existing properties and commands...

    public MainViewModel(
        // ... existing services ...
        TextureSelectionViewModel textureSelectionVM,
        MaterialSelectionViewModel materialSelectionVM,
        ORMTextureViewModel ormTextureVM,
        TextureConversionSettingsViewModel conversionSettingsVM,
        AssetLoadingViewModel assetLoadingVM) {

        TextureSelectionVM = textureSelectionVM;
        MaterialSelectionVM = materialSelectionVM;
        ORMTextureVM = ormTextureVM;
        ConversionSettingsVM = conversionSettingsVM;
        AssetLoadingVM = assetLoadingVM;

        // Wire up cross-VM events
        TextureSelectionVM.TextureSelected += OnTextureSelected;
        MaterialSelectionVM.MaterialSelected += OnMaterialSelected;
        AssetLoadingVM.AssetsLoaded += OnAssetsLoaded;
    }

    private void OnTextureSelected(object? sender, TextureSelectedEventArgs e) {
        // Load settings for selected texture
        ConversionSettingsVM.LoadSettingsForTextureCommand.Execute(e.Texture);
    }

    private void OnAssetsLoaded(object? sender, AssetsLoadedEventArgs e) {
        // Update collections
        Textures = new ObservableCollection<TextureResource>(e.Result.Textures);
        Models = new ObservableCollection<ModelResource>(e.Result.Models);
        Materials = new ObservableCollection<MaterialResource>(e.Result.Materials);

        // Generate virtual ORM textures
        ORMTextureVM.GenerateVirtualORMTextures(Textures, CurrentProjectId);
    }
}
```

---

## XAML DataContext Structure

```xml
<Window.DataContext>
    <viewmodels:MainViewModel />
</Window.DataContext>

<!-- Texture DataGrid -->
<DataGrid ItemsSource="{Binding Textures}"
          SelectedItem="{Binding TextureSelectionVM.SelectedTexture, Mode=TwoWay}">
    <i:Interaction.Triggers>
        <i:EventTrigger EventName="SelectionChanged">
            <i:InvokeCommandAction
                Command="{Binding TextureSelectionVM.SelectTextureCommand}"
                CommandParameter="{Binding TextureSelectionVM.SelectedTexture}" />
        </i:EventTrigger>
    </i:Interaction.Triggers>
</DataGrid>

<!-- Material DataGrid -->
<DataGrid ItemsSource="{Binding Materials}"
          SelectedItem="{Binding MaterialSelectionVM.SelectedMaterial, Mode=TwoWay}" />

<!-- ORM Creation Button -->
<Button Command="{Binding ORMTextureVM.CreateORMFromMaterialCommand}"
        CommandParameter="{Binding MaterialSelectionVM.SelectedMaterial}"
        Content="Create ORM from Material" />

<!-- Processing Button -->
<Button Command="{Binding ProcessTexturesCommand}"
        CommandParameter="{Binding TexturesDataGrid.SelectedItems}"
        Content="Convert Selected" />

<!-- Loading Indicator -->
<ProgressBar Value="{Binding AssetLoadingVM.LoadingProgress}"
             Maximum="{Binding AssetLoadingVM.LoadingTotal}"
             Visibility="{Binding AssetLoadingVM.IsLoading, Converter={StaticResource BoolToVisibility}}" />
```

---

## DI Registration (App.xaml.cs)

```csharp
// Register child ViewModels
services.AddTransient<TextureSelectionViewModel>();
services.AddTransient<MaterialSelectionViewModel>();
services.AddTransient<ORMTextureViewModel>();
services.AddTransient<TextureConversionSettingsViewModel>();
services.AddTransient<AssetLoadingViewModel>();

// Update MainViewModel registration
services.AddSingleton<MainViewModel>();
```

---

## Migration Checklist

### Phase 1: TextureSelectionViewModel
- [ ] Create ViewModels/TextureSelectionViewModel.cs
- [ ] Create Services/ITexturePreviewService.cs (extract from MainWindow)
- [ ] Move preview loading logic
- [ ] Update TexturesDataGrid XAML bindings
- [ ] Remove TexturesDataGrid_SelectionChanged from code-behind
- [ ] Test texture selection and preview loading

### Phase 2: ORMTextureViewModel
- [ ] Create ViewModels/ORMTextureViewModel.cs
- [ ] Create Services/IORMTextureService.cs (extract from MainWindow)
- [ ] Move ORM creation logic
- [ ] Update ORM button XAML bindings
- [ ] Remove ORM event handlers from code-behind
- [ ] Test ORM creation and deletion

### Phase 3: TextureConversionSettingsViewModel
- [ ] Create ViewModels/TextureConversionSettingsViewModel.cs
- [ ] Move settings loading/saving logic
- [ ] Update ConversionSettingsPanel bindings
- [ ] Remove settings handlers from code-behind
- [ ] Test settings persistence

### Phase 4: AssetLoadingViewModel
- [ ] Create ViewModels/AssetLoadingViewModel.cs
- [ ] Move loading logic from MainWindow.Api.cs
- [ ] Update progress indicators XAML
- [ ] Test asset loading flow

### Phase 5: MaterialSelectionViewModel
- [ ] Create ViewModels/MaterialSelectionViewModel.cs
- [ ] Move material filtering logic
- [ ] Update MaterialsDataGrid bindings
- [ ] Test material selection

---

## Expected Results

| Metric | Before | After |
|--------|--------|-------|
| MainWindow.xaml.cs lines | 4,946 | ~2,500 |
| MainWindow.Api.cs lines | 520 | ~100 |
| Business logic in code-behind | 38% | <10% |
| Testable ViewModels | 1 | 6 |
| Event handlers | 50+ | <15 |
| Commands | 9 | 25+ |

---

## UI-Specific Code to Keep in Code-Behind

The following should remain in MainWindow.xaml.cs:

1. **Column management** (resizing, reordering, visibility) - 400 lines
2. **Viewer toggles** (ShowTextureViewer, HideAllViewers) - 100 lines
3. **Layout/splitter handlers** - 50 lines
4. **Theme switching** - 30 lines
5. **DataGrid virtualization helpers** - 100 lines
6. **Drag-drop handlers** (if any) - varies
7. **Window lifecycle** (Closing, Loaded) - 50 lines

Total: ~730 lines of legitimate UI code
