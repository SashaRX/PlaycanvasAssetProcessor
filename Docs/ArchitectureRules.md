# Architecture Rules

## Overview

TexTool follows **MVVM + Service Layer** architecture:

```
┌─────────────────────────────────────────────────────────────┐
│                         View Layer                          │
│  MainWindow.xaml.cs, Windows/*.xaml.cs, UserControls/       │
│  - XAML bindings, event wiring, UI-specific code only       │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                      ViewModel Layer                        │
│  ViewModels/                                                │
│  - MainViewModel (orchestration)                            │
│  - TextureSelectionViewModel, MaterialSelectionViewModel    │
│  - AssetLoadingViewModel, ORMTextureViewModel               │
│  - TextureConversionSettingsViewModel                       │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                       Service Layer                         │
│  Services/                                                  │
│  - IPlayCanvasService, IProjectConnectionService            │
│  - IAssetLoadCoordinator, ITexturePreviewService            │
│  - ILogService, IResourceSettingsService                    │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                       Domain Layer                          │
│  Resources/ (models), TextureConversion/ (pipeline)         │
└─────────────────────────────────────────────────────────────┘
```

---

## Rules by Layer

### 1. View Layer (Code-Behind)

**ALLOWED in MainWindow.xaml.cs:**
- Event handler wiring (`Click`, `SelectionChanged`, etc.)
- Dispatcher calls for UI thread updates
- UI element manipulation (visibility, colors, animations)
- DataGrid column management (resizing, ordering, visibility)
- Theme switching
- Grid splitter handlers
- Context menu handlers
- ViewModel event subscriptions and simple handlers

**FORBIDDEN in MainWindow.xaml.cs:**
- Business logic (calculations, data transformations)
- Direct API/network calls
- File I/O operations (except via services)
- Complex data processing
- Creating domain objects directly
- Loops over large collections without delegation to services

**Pattern for event handlers:**
```csharp
// GOOD: Delegate to ViewModel
private void Button_Click(object sender, RoutedEventArgs e) {
    viewModel.SomeCommand.Execute(parameter);
}

// GOOD: Simple UI update
private void OnSomethingChanged(object? sender, EventArgs e) {
    SomePanel.Visibility = e.IsVisible ? Visibility.Visible : Visibility.Collapsed;
}

// BAD: Business logic in code-behind
private void Button_Click(object sender, RoutedEventArgs e) {
    foreach (var item in items) {
        // Processing logic here - MOVE TO SERVICE
    }
}
```

---

### 2. ViewModel Layer

**ViewModels MUST:**
- Inherit from `ObservableObject` (CommunityToolkit.Mvvm)
- Use `[ObservableProperty]` for bindable properties
- Use `[RelayCommand]` for commands
- Work only with interfaces (not concrete service implementations)
- Raise events for UI-specific actions that can't be bound

**ViewModels MUST NOT:**
- Reference UI types (`Window`, `Control`, `Dispatcher`, etc.)
- Access `App.Current` or static UI state
- Perform direct I/O or network calls (delegate to services)

**Pattern:**
```csharp
public partial class SomeViewModel : ObservableObject {
    private readonly ISomeService someService;

    [ObservableProperty]
    private string? status;

    [RelayCommand]
    private async Task DoSomethingAsync(CancellationToken ct) {
        var result = await someService.ProcessAsync(ct);
        Status = result.Message;

        // Raise event for UI-specific handling
        SomethingCompleted?.Invoke(this, new SomethingCompletedEventArgs(result));
    }

    public event EventHandler<SomethingCompletedEventArgs>? SomethingCompleted;
}
```

---

### 3. Service Layer

**Services MUST:**
- Have an interface (`IServiceName`)
- Be registered in DI (App.xaml.cs)
- Be stateless where possible (or clearly document state)
- Handle their own error logging
- Support `CancellationToken` for async operations

**Services MUST NOT:**
- Reference UI types
- Reference ViewModels
- Show message boxes or dialogs (return results, let UI handle display)

**Naming conventions:**
- `I{Domain}Service` - for service interfaces
- `{Domain}Service` - for implementations
- `{Domain}Coordinator` - for orchestrators that combine multiple services

---

### 4. Helpers

**Helpers/ folder rules:**

| Type | Location | Can Reference |
|------|----------|---------------|
| Pure utilities (no deps) | `Helpers/` | Nothing |
| Image utilities | `TextureConversion/` or `Helpers/` | SixLabors.ImageSharp |
| UI converters | `Helpers/` or `Converters/` | WPF types |
| Domain helpers | Near their domain | Domain types |

**FORBIDDEN in Helpers/:**
- Service calls
- ViewModel references
- Stateful operations
- I/O operations (except pure in-memory)

---

### 5. Resources (Domain Models)

**Resources/ contains data models only:**
- `TextureResource`, `ModelResource`, `MaterialResource`
- `ORMTextureResource`
- Base classes (`BaseResource`)

**Rules:**
- No business logic in models
- Use `INotifyPropertyChanged` for bindable properties
- Keep serialization-related code minimal

---

### 6. TextureConversion Pipeline

**Structure:**
```
TextureConversion/
├── Core/           - Types, enums, settings records
├── MipGeneration/  - Mipmap generation, filters
├── BasisU/         - KTX CLI wrapper
├── Pipeline/       - Main orchestrator
├── Settings/       - Preset management
├── Analysis/       - Histogram analysis
└── KVD/            - Metadata handling
```

**Rules:**
- `Core/` has no dependencies on other subfolders
- `Pipeline/` orchestrates, doesn't implement low-level operations
- `Settings/` handles persistence, not processing
- Each subfolder can only depend on `Core/` and external libs

---

## DI Registration Pattern

```csharp
// App.xaml.cs
private void ConfigureServices(ServiceCollection services) {
    // 1. Infrastructure services (logging, file system)
    services.AddSingleton<ILogService, LogService>();
    services.AddSingleton<IFileSystem, FileSystem>();

    // 2. Domain services
    services.AddSingleton<IPlayCanvasService, PlayCanvasService>();
    services.AddSingleton<IProjectConnectionService, ProjectConnectionService>();

    // 3. ViewModels (with dependencies)
    services.AddSingleton<TextureSelectionViewModel>();
    services.AddSingleton<MainViewModel>();

    // 4. Main window (last)
    services.AddSingleton<MainWindow>();
}
```

---

## Testing Guidelines

**Testable:**
- Services (mock dependencies via interfaces)
- ViewModels (mock services)
- Pure helpers
- Pipeline components

**Not easily testable (UI):**
- Code-behind event handlers
- XAML bindings
- DataGrid column management

**Test file location:**
- `AssetProcessor.Tests/Services/` - service tests
- `AssetProcessor.Tests/ViewModels/` - ViewModel tests (if created)
- `AssetProcessor.Tests/` - integration tests

---

## Migration Checklist

When moving code from code-behind to ViewModel/Service:

1. [ ] Identify the responsibility (business logic vs UI)
2. [ ] Create interface if it's a service
3. [ ] Implement in appropriate layer
4. [ ] Register in DI
5. [ ] Update ViewModel to use new service/expose command
6. [ ] Replace code-behind with event wiring or binding
7. [ ] Add/update tests
8. [ ] Remove dead code from code-behind

---

## Current State (as of 2025-01)

| Component | Lines | Status |
|-----------|-------|--------|
| MainWindow.xaml.cs | ~4800 | Needs reduction to ~2500 |
| MainWindow.Api.cs | ~400 | Merge into services |
| ViewModels/ | ~1500 | Good structure |
| Services/ | ~3000 | Well organized |

**Target:** Reduce MainWindow.xaml.cs to UI-only code (~2500 lines)
