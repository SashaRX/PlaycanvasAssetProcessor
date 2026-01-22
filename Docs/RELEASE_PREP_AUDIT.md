# Аудит кода перед первым релизом

**Дата:** 2026-01-22
**Версия:** Pre-release audit

---

## Краткое резюме

| Категория | Критических | Высоких | Средних | Низких |
|-----------|-------------|---------|---------|--------|
| Дубликаты кода | 2 | 3 | 4 | 2 |
| Костыли/Workarounds | 1 | 3 | 5 | 3 |
| Баги и уязвимости | 4 | 6 | 6 | 4 |
| Мёртвый код | 0 | 2 | 3 | 5 |
| Архитектура | 4 | 5 | 3 | 2 |

**Общая оценка:** Код функционален, но требует рефакторинга перед релизом. Критические проблемы связаны с утечками ресурсов и God-классами.

---

## 1. КРИТИЧЕСКИЕ ПРОБЛЕМЫ (исправить до релиза)

### 1.1 Утечка ресурсов в B2UploadService.cs

**Файл:** `Upload/B2UploadService.cs:69, 639`

```csharp
// Проблема: двойной Dispose
_uploadSemaphore.Dispose();  // Line 69 в AuthorizeAsync
// ...
public void Dispose() {
    _httpClient.Dispose();
    _uploadSemaphore.Dispose();  // Line 639 - повторный dispose!
}
```

**Решение:**
```csharp
private bool _disposed = false;

public void Dispose() {
    if (_disposed) return;
    _disposed = true;
    _httpClient.Dispose();
    _uploadSemaphore.Dispose();
}
```

---

### 1.2 Утечка Process в BasisUWrapper.cs

**Файл:** `TextureConversion/BasisU/BasisUWrapper.cs:83-119`

```csharp
// Проблема: Process не в using
var process = new Process { ... };
await process.WaitForExitAsync();
return new BasisUResult { ... };
// Process НИКОГДА не освобождается!
```

**Решение:**
```csharp
using var process = new Process { ... };
```

---

### 1.3 Path Traversal уязвимость в LocalCacheService.cs

**Файл:** `Services/LocalCacheService.cs:26-42`

```csharp
// Проблема: folderPath не валидируется
targetFolder = Path.Combine(targetFolder, folderPath);
// folderPath может быть "../../.." или "C:\Windows"
```

**Решение:**
```csharp
if (parentId.HasValue && folderPaths.TryGetValue(parentId.Value, out string? folderPath)) {
    // Валидация: путь не должен выходить за пределы assetsFolder
    string combined = Path.GetFullPath(Path.Combine(targetFolder, folderPath));
    if (!combined.StartsWith(assetsFolder, StringComparison.OrdinalIgnoreCase)) {
        throw new ArgumentException($"Invalid folder path: {folderPath}");
    }
    targetFolder = combined;
}
```

---

### 1.4 SemaphoreSlim утечка в BasisUWrapper.cs

**Файл:** `TextureConversion/BasisU/BasisUWrapper.cs:426-428`

```csharp
// Проблема: semaphore не освобождается
var semaphore = new SemaphoreSlim(...);
// ... использование ...
return results;  // semaphore НЕ disposed!
```

**Решение:**
```csharp
await using var semaphore = new SemaphoreSlim(...);
// или
using var semaphore = new SemaphoreSlim(...);
try { ... }
finally { semaphore.Dispose(); }
```

---

## 2. GOD-КЛАССЫ (рефакторинг после релиза)

### 2.1 MainWindow.xaml.cs — 6,004 строки

**Проблема:** Класс отвечает за:
- UI рендеринг
- D3D11 рендеринг
- Загрузку ассетов
- Превью текстур
- Просмотр моделей
- Редактор шейдеров
- Управление DataGrid
- Темы
- 20+ зависимостей в конструкторе

**Рекомендация:** Разделить на:
- `MainWindow.xaml.cs` — только XAML code-behind
- `TexturePreviewPresenter.cs` — логика превью текстур
- `ModelViewerPresenter.cs` — логика просмотра моделей
- `AssetGridPresenter.cs` — логика DataGrid

### 2.2 ModelExportPipeline.cs — 1,747 строк

**Проблема:** Прямое создание зависимостей:
```csharp
_modelConversionPipeline = new ModelConversionPipeline(...);
_textureConversionPipeline = new TextureConversionPipeline(...);
```

**Рекомендация:** Внедрить через DI:
```csharp
public ModelExportPipeline(
    IModelConversionPipeline modelConversion,
    ITextureConversionPipeline textureConversion,
    IChannelPackingPipeline channelPacking,
    IMaterialJsonGenerator materialGenerator)
```

---

## 3. ДУБЛИКАТЫ КОДА

### 3.1 PresetManager и ORMPresetManager — 400+ LOC дубликатов

**Файлы:**
- `TextureConversion/Settings/PresetManager.cs`
- `TextureConversion/Settings/ORMPresetManager.cs`

**Дублируются методы:**
- `GetPreset(string name)` — 8 мест с одинаковым LINQ
- `AddPreset()`, `UpdatePreset()`, `DeletePreset()`
- `LoadPresets()`, `SavePresets()`

**Решение:** Создать базовый класс:
```csharp
public abstract class GenericPresetManager<T> where T : class, IPreset
{
    protected abstract string PresetsFilePath { get; }
    protected abstract IReadOnlyList<T> GetBuiltInPresets();

    public T? GetPreset(string name) =>
        _presets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    // Общая логика Add/Update/Delete/Load/Save
}
```

### 3.2 Дубликат паттерна поиска пресета

**8 мест с одинаковым кодом:**
```csharp
.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
```

**Решение:** Extension method:
```csharp
public static T? FindByNameIgnoreCase<T>(this IEnumerable<T> presets, string name)
    where T : IPreset
    => presets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
```

### 3.3 Дубликат загрузки колонок DataGrid

**Файл:** `MainWindow.xaml.cs:205-212`

```csharp
LoadColumnOrder(TexturesDataGrid);
LoadColumnOrder(ModelsDataGrid);
LoadColumnOrder(MaterialsDataGrid);
LoadColumnWidths(TexturesDataGrid);
LoadColumnWidths(ModelsDataGrid);
LoadColumnWidths(MaterialsDataGrid);
```

**Решение:**
```csharp
var dataGrids = new[] { TexturesDataGrid, ModelsDataGrid, MaterialsDataGrid };
foreach (var grid in dataGrids) {
    LoadColumnOrder(grid);
    LoadColumnWidths(grid);
}
```

---

## 4. МЁРТВЫЙ КОД (удалить)

### 4.1 LEGACY методы в MainWindow.TextureViewerUI.cs

**Файл:** `MainWindow.TextureViewerUI.cs:307-412`

13 пустых stub-методов:
- `ScheduleFitZoomUpdate()` — пустой
- `RecalculateFitZoom()` — пустой
- `UpdateTransform()` — пустой
- `ApplyFitZoom()` — пустой
- `ApplyZoomWithPivot()` — пустой
- `SetZoomAndCenter()` — пустой
- `ResetPan()` — пустой
- `ToImageSpace()` — возвращает параметр без изменений
- `GetViewportCenterInImageSpace()` — возвращает (0,0)
- `StartPanning()` — пустой
- `StopPanning()` — пустой
- `ApplyPanDelta()` — пустой

**Действие:** Удалить все эти методы.

### 4.2 Закомментированный код

**Файл:** `MainWindow.TextureViewerUI.cs:293-305, 497-531`

Большие блоки закомментированного кода для удалённых UI контролов.

**Действие:** Удалить.

### 4.3 Дубликат SizeConverter.Convert()

**Файл:** `Helpers/Converters.cs:9-55`

3 метода делают одно и то же:
- Line 9-27: `Convert(object, Type, object, CultureInfo)`
- Line 29-47: `static Convert(object)`
- Line 53-55: `Convert(object, object, object, object)` — throws NotImplementedException

**Действие:** Оставить только IValueConverter реализацию, удалить остальные.

### 4.4 Obsolete методы в IMasterMaterialService.cs

**Файл:** `Services/IMasterMaterialService.cs:114-140`

4 метода помечены `[Obsolete]`:
- `GetChunksFolderPath(string)`
- `LoadChunkFromFileAsync(string, string, CancellationToken)`
- `SaveChunkToFileAsync(string, ShaderChunk, CancellationToken)`
- `DeleteChunkFileAsync(string, string, CancellationToken)`

**Действие:** Проверить использование, если нигде не используются — удалить.

---

## 5. TODO/FIXME (реализовать или удалить)

### 5.1 Нереализованные UI handlers

**Файл:** `MainWindow.xaml.cs:5315-5350`

8 методов с `// TODO: Implement`:
```csharp
// TODO: Implement delete exported texture file
// TODO: Implement delete exported model file
// TODO: Implement delete exported material file
// TODO: Implement ORM group pack & convert
// TODO: Implement ORM group upload
// TODO: Implement ORM group delete exported
// TODO: Implement ORM group open folder
// TODO: Implement delete all exported files
```

**Решение для релиза:**
1. Либо реализовать
2. Либо скрыть соответствующие кнопки в UI
3. Либо показать MessageBox "Not implemented"

### 5.2 BatchProcessor не поддерживает Toksvig

**Файл:** `TextureConversion/Pipeline/BatchProcessor.cs:96`

```csharp
toksvigSettings: null, // TODO: Add toksvigSettings parameter
```

**Решение:** Добавить параметр или документировать ограничение.

### 5.3 NativeMeshSimplifier.WriteGlb — stub

**Файл:** `ModelConversion/Native/NativeMeshSimplifier.cs:268`

```csharp
// TODO: Реализовать полную запись GLB с обновлёнными индексами
```

Функция просто копирует оригинальный GLB.

**Решение:** Либо реализовать, либо удалить неработающую функцию.

---

## 6. WORKAROUNDS (документировать)

### 6.1 Alt+Tab freeze fix

**Файлы:**
- `MainWindow.D3D11Viewer.cs:50-154`
- `CLAUDE.md` — уже документировано

**Статус:** Это намеренный workaround, хорошо документирован. Оставить.

### 6.2 Task.Delay workarounds

**12 мест с Task.Delay:**
- `MainWindow.D3D11Viewer.cs:139, 143` — 200ms delay для стабильности фокуса
- `MainWindow.xaml.cs:956` — 100ms после rebinding DataGrid
- `MainWindow.xaml.cs:2541` — 200ms перед ORM histogram
- `Services/TextureProcessingService.cs:139` — 300ms после назначения пресета

**Рекомендация:** Документировать причины в комментариях.

### 6.3 Reflection для настроек колонок

**Файл:** `MainWindow.xaml.cs:2195-2232, 2712-2717`

```csharp
typeof(AppSettings).GetProperty(settingName)?.GetValue(AppSettings.Default)
```

**Рекомендация:** Заменить на типизированный словарь или отдельный класс настроек UI.

---

## 7. RACE CONDITIONS

### 7.1 SharedProgressState не атомарный

**Файл:** `Services/Models/SharedProgressState.cs:38-40`

```csharp
public void Update(int current, int total, string? currentAsset = null) {
    _current = current;      // Не атомарно
    _total = total;          // Не атомарно
    _currentAsset = currentAsset;
}
```

**Решение:**
```csharp
private readonly object _lock = new();

public void Update(int current, int total, string? currentAsset = null) {
    lock (_lock) {
        _current = current;
        _total = total;
        _currentAsset = currentAsset;
    }
}
```

### 7.2 Fire-and-forget tasks

**Файл:** `Services/ProjectFileWatcherService.cs:113, 130`

```csharp
Task.Delay(DebounceDelayMs).ContinueWith(_ => { ... });  // Не awaited!
```

**Решение:** Добавить обработку исключений:
```csharp
_ = Task.Delay(DebounceDelayMs).ContinueWith(async _ => {
    try { ... }
    catch (Exception ex) { logger.Error(ex, "Debounce task failed"); }
}, TaskScheduler.Default);
```

---

## 8. UI/SERVICE СМЕШЕНИЕ

### 8.1 HistogramService возвращает OxyPlot типы

**Файл:** `Services/HistogramService.cs`

```csharp
public void AddSeriesToModel(PlotModel model, ...)  // UI тип!
```

**Решение:** Сервис должен возвращать данные:
```csharp
public HistogramData CalculateHistogram(...)
// UI слой создаёт PlotModel из HistogramData
```

### 8.2 TextureChannelService возвращает BitmapSource

**Файл:** `Services/TextureChannelService.cs`

```csharp
Task<BitmapSource> ApplyChannelFilterAsync(...)  // WPF тип!
```

**Решение:** Возвращать `byte[]` или `Image<Rgba32>`, конвертировать в UI слое.

---

## 9. ПЛАН ДЕЙСТВИЙ

### До релиза (критические)

1. [ ] Исправить двойной Dispose в B2UploadService
2. [ ] Добавить using для Process в BasisUWrapper
3. [ ] Исправить Path Traversal в LocalCacheService
4. [ ] Dispose SemaphoreSlim в BasisUWrapper.EncodeBatchAsync
5. [ ] Удалить LEGACY stub методы из MainWindow.TextureViewerUI.cs
6. [ ] Удалить закомментированный код
7. [ ] Скрыть или реализовать TODO кнопки

### После релиза v1.0 (рефакторинг)

1. [ ] Разделить MainWindow на презентеры
2. [ ] Создать GenericPresetManager<T>
3. [ ] Вынести UI типы из сервисов
4. [ ] Добавить интерфейсы для pipeline компонентов
5. [ ] Исправить race condition в SharedProgressState
6. [ ] Заменить reflection на типизированные настройки

### Backlog

1. [ ] Реализовать NativeMeshSimplifier.WriteGlb
2. [ ] Добавить Toksvig в BatchProcessor
3. [ ] Реализовать FlipUVs в GltfPackWrapper

---

## 10. ФАЙЛЫ ДЛЯ УДАЛЕНИЯ/ОЧИСТКИ

| Файл | Действие | Строки |
|------|----------|--------|
| MainWindow.TextureViewerUI.cs | Удалить LEGACY методы | 307-412 |
| MainWindow.TextureViewerUI.cs | Удалить комментарии | 293-305, 497-531 |
| Helpers/Converters.cs | Удалить дубликаты | 29-47, 53-55 |
| glblod_debug.txt | Уже удалён | — |
| internal-nlog.txt | В .gitignore | — |
| warning_log.txt | В .gitignore | — |

---

## 11. МЕТРИКИ КОДА

| Метрика | Значение | Рекомендация |
|---------|----------|--------------|
| Самый большой класс | MainWindow.xaml.cs (6004 LOC) | <500 LOC |
| Макс. параметров конструктора | 20 (MainWindow) | <5 |
| Дубликатов кода | ~400 LOC | 0 |
| TODO/FIXME | 12 | 0 |
| Мёртвого кода | ~200 LOC | 0 |
| Пустых catch блоков | 1 | 0 |

---

*Отчёт сгенерирован автоматически. Рекомендуется ревью командой перед применением изменений.*
