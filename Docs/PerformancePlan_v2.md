# 📋 План оптимизации производительности (Актуализированный)

> **Дата обновления:** 2025-11-10
> **Версия:** 2.0
> **Базовая ветка:** main (commit 0b80790)

---

## 🎯 Текущее состояние

### ✅ Уже реализовано (main ветка)

1. **Polly Retry Policy** ✅ (commit 9de2358)
   - Экспоненциальный backoff для transient failures
   - HttpResponseMessage disposal в onRetryAsync callback
   - Retry на 5xx и 429 статусы

2. **IAsyncEnumerable для streaming** ✅ (commit 694a5b2)
   - `GetAssetsAsync` возвращает `IAsyncEnumerable<PlayCanvasAssetSummary>`
   - Пагинация по 200 элементов
   - Меньше потребление памяти

3. **Типизированные модели** ✅ (commit 694a5b2)
   - `PlayCanvasAssetSummary`, `PlayCanvasAssetDetail`, `PlayCanvasAssetFileInfo`
   - Замена `JObject`/`JArray` на `System.Text.Json`

4. **Secure API key storage** ✅ (commit 0dba0f0)
   - DPAPI для Windows, AES-256 для Linux/macOS
   - Автоматическая миграция plaintext ключей

### ❌ НЕ реализовано / Проблемы

---

## 🔴 Критические проблемы (main ветка)

### 1. HttpClient создается для каждого запроса в ImageHelper ⚠️ CRITICAL

**Текущий код:**
```csharp
// Helpers/ImageHelper.cs:14
public static async Task<(int Width, int Height)> GetImageResolutionAsync(...) {
    using HttpClient client = new();  // ❌ Создается каждый раз!
    client.DefaultRequestHeaders.Authorization = ...;
    // Ещё 2 места: строки 67, 87
}
```

**Проблема:**
- Socket exhaustion при массовых запросах
- Игнорирование DNS TTL
- Медленное установление TCP-соединений

**Решение 1: Использовать существующий PlayCanvasService** (быстро)
```csharp
// Добавить в IPlayCanvasService:
Task<(int Width, int Height)> GetImageResolutionAsync(string url, CancellationToken ct);

// ImageHelper:
public static Task<(int Width, int Height)> GetImageResolutionAsync(
    IPlayCanvasService service,
    string url,
    CancellationToken ct) => service.GetImageResolutionAsync(url, ct);
```

**Решение 2: Shared HttpClient через IHttpClientFactory** (правильно)
```csharp
// Program.cs / App.xaml.cs:
services.AddHttpClient<IPlayCanvasService, PlayCanvasService>(client => {
    client.Timeout = TimeSpan.FromSeconds(30);
});

services.AddHttpClient("ImageHelper", client => {
    client.Timeout = TimeSpan.FromSeconds(10);
});
```

**Приоритет:** 🔴 CRITICAL
**Ожидаемое ускорение:** ⚡ 3-5x для resolution fetching

---

### 2. PlayCanvasService создается через `new` вместо DI ⚠️ CRITICAL

**Текущий код:**
```csharp
// MainWindow.xaml.cs:138
private readonly PlayCanvasService playCanvasService = new();
```

**Проблема:**
- Каждый экземпляр создает свой HttpClient
- Нет возможности внедрить shared HttpClient
- Нарушает принципы SOLID

**Решение: Dependency Injection**
```csharp
// App.xaml.cs:
public partial class App : Application {
    private IServiceProvider? serviceProvider;

    protected override void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);

        var services = new ServiceCollection();

        // HttpClient
        services.AddHttpClient<IPlayCanvasService, PlayCanvasService>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = 50
            });

        // Services
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        serviceProvider = services.BuildServiceProvider();

        var mainWindow = serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }
}

// MainWindow.xaml.cs: Constructor injection
public MainWindow(IPlayCanvasService playCanvasService, MainViewModel viewModel) {
    this.playCanvasService = playCanvasService;
    DataContext = viewModel;
    InitializeComponent();
}
```

**Приоритет:** 🔴 CRITICAL
**Ожидаемое ускорение:** ⚡ 2-3x для API calls

---

### 3. Отсутствует кеширование разрешения текстур ⚠️ HIGH

**Текущая проблема:**
- Для каждой "On Server" текстуры делается HTTP запрос
- Для 500 текстур = 500 HTTP запросов (10-30 секунд)

**Решение A: Извлечение из PlayCanvas API ответа**
```csharp
// В GetAssetsAsync уже есть данные!
// asset.File.Variants["webp"]["width"] и ["height"]

// Services/PlayCanvasService.cs - ParseAsset:
private static PlayCanvasAssetSummary ParseAsset(JsonElement element, string url) {
    // ... existing code ...

    // Extract resolution from API response if available
    int? width = null;
    int? height = null;
    if (fileElement.TryGetProperty("variants", out var variants)) {
        foreach (var variant in variants.EnumerateObject()) {
            if (variant.Value.TryGetProperty("width", out var w) &&
                variant.Value.TryGetProperty("height", out var h)) {
                width = w.GetInt32();
                height = h.GetInt32();
                break;
            }
        }
    }

    return new PlayCanvasAssetSummary(..., width, height, ...);
}
```

**Решение B: SQLite кеш для метаданных**
```csharp
// Services/AssetMetadataCache.cs
public class AssetMetadataCache {
    private readonly SqliteConnection db;

    public async Task<(int Width, int Height)?> GetResolutionAsync(string assetUrl) {
        var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT width, height FROM texture_metadata WHERE url = @url";
        cmd.Parameters.AddWithValue("@url", assetUrl);
        // ...
    }

    public async Task SaveResolutionAsync(string assetUrl, int width, int height) {
        var cmd = db.CreateCommand();
        cmd.CommandText =
            "INSERT OR REPLACE INTO texture_metadata (url, width, height, timestamp) " +
            "VALUES (@url, @w, @h, @t)";
        // ...
    }
}
```

**Приоритет:** 🔴 HIGH
**Ожидаемое ускорение:** ⚡ Устраняет 500+ HTTP запросов (20-40 секунд экономии)

---

### 4. Избыточные Dispatcher.Invoke для Progress ⚠️ MEDIUM

**Текущая проблема:**
```csharp
// MainWindow.xaml.cs:3708, 3762, etc.
IProgress<int> progress = new Progress<int>(_ => Dispatcher.Invoke(() => {
    ProgressBar.Value++;  // ❌ Вызывается для КАЖДОГО ассета!
    ProgressTextBlock.Text = $"{ProgressBar.Value}/{ProgressBar.Maximum}";
}));
```

**Решение: Batching progress updates**
```csharp
// Helpers/ThrottledProgress.cs
public class ThrottledProgress<T> : IProgress<T>, IDisposable {
    private readonly IProgress<T> inner;
    private readonly Timer timer;
    private int pendingReports;

    public ThrottledProgress(IProgress<T> innerProgress, int intervalMs = 100) {
        inner = innerProgress;
        timer = new Timer(_ => Flush(), null, intervalMs, intervalMs);
    }

    public void Report(T value) {
        if (value is int count) {
            Interlocked.Add(ref pendingReports, count);
        }
    }

    private void Flush() {
        int current = Interlocked.Exchange(ref pendingReports, 0);
        if (current > 0) {
            inner.Report((T)(object)current);
        }
    }

    public void Dispose() => timer?.Dispose();
}

// Использование:
var uiProgress = new Progress<int>(count => Dispatcher.Invoke(() => {
    ProgressBar.Value += count;
    ProgressTextBlock.Text = $"{ProgressBar.Value}/{ProgressBar.Maximum}";
}));

using var throttled = new ThrottledProgress<int>(uiProgress, intervalMs: 100);
// UI обновляется каждые 100ms вместо тысяч раз
```

**Приоритет:** 🟡 MEDIUM
**Ожидаемое ускорение:** ⚡ Сокращает Dispatcher calls в 50-100x (5-10 секунд)

---

### 5. MD5 хеш-проверка читает весь файл ⚠️ LOW

**Текущая проблема:**
```csharp
// FileHelper.IsFileIntact читает весь файл
// Для 50MB файла = 100-200ms на HDD
```

**Решение: Quick validation**
```csharp
public static async Task<bool> QuickVerifyFileAsync(
    string filePath,
    long expectedSize,
    string? expectedHash = null) {

    FileInfo info = new(filePath);

    // 1. Быстрая проверка размера (1ms)
    if (info.Length != expectedSize) return false;

    // 2. Если хеш не требуется - считаем достаточным
    if (string.IsNullOrEmpty(expectedHash)) return true;

    // 3. Проверяем только начало и конец файла (5ms vs 200ms)
    const int chunkSize = 65536; // 64KB
    await using var stream = File.OpenRead(filePath);

    using var md5 = MD5.Create();
    byte[] buffer = new byte[chunkSize];

    // Начало
    int read = await stream.ReadAsync(buffer);
    md5.TransformBlock(buffer, 0, read, null, 0);

    // Конец
    if (info.Length > chunkSize) {
        stream.Seek(-chunkSize, SeekOrigin.End);
        read = await stream.ReadAsync(buffer);
        md5.TransformFinalBlock(buffer, 0, read);
    }

    string quickHash = BitConverter.ToString(md5.Hash!).Replace("-", "");
    return quickHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
}
```

**Приоритет:** 🟢 LOW (используется редко)
**Ожидаемое ускорение:** ⚡ 20-50x для больших файлов

---

## 🏗️ Архитектурные улучшения

### 6. MVVM нарушения ⚠️ CRITICAL

**Текущие проблемы:**
```csharp
// MainWindow.xaml.cs:
public ObservableCollection<TextureResource> Textures { get; } = [];  // ❌ Владелец - Window
public MainViewModel ViewModel { get; }  // ViewModel используется как контейнер для ссылок

// DataContext = this - окно само себе DataContext!
```

**Последствия:**
- Невозможно тестировать логику без UI
- Дублирование состояния между Window и ViewModel
- Нарушение Single Responsibility

**Решение: Правильный MVVM**
```csharp
// MainViewModel.cs - ВЛАДЕЕТ данными
public class MainViewModel : ObservableObject {
    public ObservableCollection<TextureResource> Textures { get; } = [];
    public ObservableCollection<ModelResource> Models { get; } = [];
    public ObservableCollection<MaterialResource> Materials { get; } = [];

    [RelayCommand]
    private async Task ConnectAsync() { /* логика подключения */ }

    [RelayCommand]
    private async Task LoadAssetsAsync() { /* логика загрузки */ }
}

// MainWindow.xaml.cs - ТОЛЬКО UI логика
public MainWindow(MainViewModel viewModel) {
    DataContext = viewModel;  // ✅ ViewModel - источник данных
    InitializeComponent();

    // Только UI-специфичная логика:
    // - Preview rendering
    // - Drag&drop
    // - Context menus
}

// MainWindow.xaml - Bindings к ViewModel
<DataGrid ItemsSource="{Binding Textures}" />
<Button Command="{Binding ConnectCommand}" />
```

**Приоритет:** 🔴 CRITICAL для поддерживаемости
**Польза:** Тестируемость, модульность, separation of concerns

---

### 7. RecalculateIndices вызывает полную перерисовку ⚠️ MEDIUM

**Текущая проблема:**
```csharp
// MainWindow.xaml.cs:4242
private void RecalculateIndices() {
    Dispatcher.Invoke(() => {
        // Обновляем индексы
        int index = 1;
        foreach (var texture in textures) {
            texture.Index = index++;
        }
        TexturesDataGrid.Items.Refresh();  // ❌ Полная перерисовка!
        // То же для Models и Materials
    });
}
```

**Решение: INotifyPropertyChanged на Index**
```csharp
// BaseResource.cs
private int _index;
public int Index {
    get => _index;
    set => SetProperty(ref _index, value);  // CommunityToolkit.Mvvm
}

// Теперь RecalculateIndices не нужен - каждая строка обновится автоматически!
// Или отложенное обновление:
Dispatcher.BeginInvoke(() => {
    TexturesDataGrid.Items.Refresh();
}, DispatcherPriority.Background);
```

**Приоритет:** 🟡 MEDIUM
**Ожидаемое ускорение:** ⚡ Устраняет UI freezes при больших списках

---

### 8. Отсутствует виртуализация DataGrid ⚠️ HIGH

**Проверить в MainWindow.xaml:**
```xml
<DataGrid x:Name="TexturesDataGrid"
          VirtualizingPanel.IsVirtualizing="True"
          VirtualizingPanel.VirtualizationMode="Recycling"
          EnableRowVirtualization="True"
          EnableColumnVirtualization="True">
```

**Если отсутствует - добавить!**

**Приоритет:** 🔴 HIGH
**Ожидаемое ускорение:** ⚡ UI responsive с тысячами элементов

---

## 📊 План внедрения

### Фаза 1: Критичные быстрые победы (1-2 дня)

**Приоритет:** 🔴 CRITICAL

1. ✅ **DI контейнер** (4 часа)
   - Microsoft.Extensions.DependencyInjection
   - IHttpClientFactory для PlayCanvasService
   - Constructor injection в MainWindow

2. ✅ **Shared HttpClient для ImageHelper** (2 часа)
   - Переиспользовать PlayCanvasService.client
   - Или IHttpClientFactory

3. ✅ **Извлечение resolution из API** (2 часа)
   - Парсинг variants в GetAssetsAsync
   - Устранение 500+ HTTP запросов

4. ✅ **Throttled Progress** (1 час)
   - ThrottledProgress<T> helper
   - Замена всех Progress<int> вызовов

**Ожидаемый результат:** ⚡ 5-10x ускорение загрузки ассетов

---

### Фаза 2: Архитектурные улучшения (3-5 дней)

**Приоритет:** 🔴 CRITICAL для maintainability

5. ✅ **Правильный MVVM** (8 часов)
   - Переместить коллекции в MainViewModel
   - Команды вместо event handlers
   - DataContext = viewModel

6. ✅ **DataGrid виртуализация** (1 час)
   - Проверить и включить в XAML

7. ✅ **RecalculateIndices оптимизация** (2 часа)
   - INotifyPropertyChanged на Index
   - Background priority Refresh

**Ожидаемый результат:** Responsive UI, тестируемость

---

### Фаза 3: Дополнительные оптимизации (опционально)

**Приоритет:** 🟢 LOW-MEDIUM

8. ✅ **SQLite metadata cache** (1 день)
   - Persistent кеш разрешений
   - Быстрый startup при повторном запуске

9. ✅ **Quick file verification** (2 часа)
   - Chunk-based MD5
   - Только для critical paths

10. ✅ **FileSystemCache** (3 часа)
    - Кеширование Directory.EnumerateFiles
    - Пакетная проверка File.Exists

---

## 📈 Ожидаемые результаты

### До оптимизации (текущее состояние)
- Загрузка 500 ассетов: **~40-80 секунд**
- Проверка локальных файлов: **~20-40 секунд**
- UI freezes при обновлении больших списков

### После Фазы 1
- Загрузка 500 ассетов: **~8-15 секунд** ⚡ 5-6x
- Проверка локальных файлов: **~20-40 секунд** (без изменений)
- UI freezes: сокращены на 80%

### После Фазы 2
- Загрузка 500 ассетов: **~5-10 секунд** ⚡ 8-10x
- Проверка локальных файлов: **~20-40 секунд** (без изменений)
- UI freezes: **устранены**
- **Тестируемость:** можно писать unit tests для MainViewModel

### После Фазы 3 (опционально)
- Загрузка 500 ассетов: **~3-5 секунд** ⚡ 12-15x
- Проверка локальных файлов: **~2-5 секунд** ⚡ 10-20x
- **Persistent cache:** мгновенный startup при повторном запуске

---

## ⚠️ Важные замечания

### Для ORM ветки (claude/orm-packing-gltf-playcanvas-*)

**КРИТИЧНО:** Ветка базируется на старом коде ДО httpclient-retries merge!

**Необходимо:**
1. Merge main → ORM branch
2. Разрешить конфликты
3. Применить все оптимизации к merged версии

**Отсутствует в ORM ветке:**
- ❌ Polly retry policy
- ❌ IAsyncEnumerable streaming
- ❌ PlayCanvasModels (типизированные модели)
- ❌ Secure API key storage improvements

---

## 🔗 Связанные документы

- [Security.md](Security.md) - Secure API key storage
- [TextureViewerSpec.md](TextureViewerSpec.md) - D3D11 viewer architecture
- [BuildOptimizations.md](BuildOptimizations.md) - Build optimizations

---

**Автор:** Claude Code
**Дата создания:** 2025-11-10
**Последнее обновление:** 2025-11-10
