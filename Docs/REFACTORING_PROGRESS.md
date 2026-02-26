# Прогресс рефакторинга MainWindow

**Дата анализа:** 2026-02-24
**Состояние:** Рефакторинг в активной фазе — основная декомпозиция выполнена, тесты написаны для workflow-координаторов, остаётся техдолг и финальная очистка.

---

## Обзор

Монолитный `MainWindow` прошёл масштабную декомпозицию по трём направлениям:
1. **Координаторы** — бизнес-логика извлечена в 9 тестируемых сервисов
2. **ViewModels** — 13 ViewModel'ов для UI-состояния (5,367 строк)
3. **UserControls** — 12 контролов для UI-панелей

Все 5 фаз из `MVVM_Refactoring_Plan.md` реализованы (сам документ не обновлён).

---

## 1. MainWindow: метрики декомпозиции

### Текущая структура
- **28 partial-файлов** (.cs), суммарно **9,102 строки**
- Основной файл `MainWindow.xaml.cs`: **727 строк** (конструктор, инициализация, DI)
- Остальные partial-файлы: **8,375 строк** (обработчики UI-событий)

### Конструктор (10 зависимостей через DI)

| Категория | Зависимости |
|-----------|-------------|
| Фасады (3) | ConnectionServiceFacade, AssetDataServiceFacade, TextureViewerServiceFacade |
| Координаторы (4) | ConnectionWorkflow, AssetWorkflow, PreviewWorkflow, UploadWorkflow |
| Сервисы (3) | AppSettings, ILogService, IDataGridLayoutService |

### Извлечённые UserControls (12 шт.)

Директория `Controls/`:
- ConnectionPanel, ServerAssetsPanel, ServerFileInfoPanel
- TextureConversionSettingsPanel, ORMPackingPanel, HistogramPanel
- MaterialInfoPanel, MasterMaterialsEditorPanel, ChunkSlotsPanel
- ModelConversionSettingsPanel, ExportToolsPanel, LogViewerControl

---

## 2. Координаторы

### Workflow-координаторы (чистая бизнес-логика, отличное покрытие)

| Координатор | Строки | Тесты | Назначение |
|-------------|--------|-------|------------|
| ConnectionWorkflowCoordinator | 102 | 15 | Подключение к проекту, оценка обновлений |
| AssetWorkflowCoordinator | 280 | 14 | URL-извлечение, синхронизация статусов, навигация |
| UploadWorkflowCoordinator | 282 | 7 | Валидация B2, сбор файлов, маппинг |
| PreviewWorkflowCoordinator | 161 | 9 | Загрузка превью, группировка, ORM-извлечение |

**Итого workflow:** 825 строк, 45 тестов

### Asset-координаторы (тяжёлые операции, недостаточное покрытие)

| Координатор | Строки | Тесты | Статус покрытия |
|-------------|--------|-------|-----------------|
| AssetLoadCoordinator | 441 | 0 | **Критический пробел** — загрузка из JSON, throttling |
| AssetUploadCoordinator | 473 | 0 | **Критический пробел** — B2 upload, batch |
| AssetDownloadCoordinator | 164 | 3 | Частичное покрытие |

**Итого asset:** 1,078 строк, 3 теста

### Утилитарные координаторы

| Координатор | Строки | Тесты | Назначение |
|-------------|--------|-------|------------|
| HistogramCoordinator | 92 | 3 | Обёртка IHistogramService для UI |
| PreviewRendererCoordinator | 21 | 1 | Переключение D3D11 preview |

**Итого по всем координаторам:** 2,016 строк, 52 теста

---

## 3. ViewModels (5 фаз MVVM-плана реализованы)

| ViewModel | Строки | Фаза плана |
|-----------|--------|-----------|
| MainViewModel | 1,114 | Базовый (до плана) |
| AssetLoadingViewModel | 1,031 | Phase 4 |
| MasterMaterialsViewModel | 809 | — |
| ORMTextureViewModel | 463 | Phase 2 |
| TextureConversionViewModel | 458 | — |
| TextureConversionSettingsViewModel | 293 | Phase 3 |
| TextureSelectionViewModel | 269 | Phase 1 |
| MasterMaterialEditorViewModel | 200 | — |
| MaterialSelectionViewModel | 183 | Phase 5 |
| ServerAssetViewModel | 164 | — |
| ChunkSlotViewModel | 136 | — |
| ChunkEditorViewModel | 128 | — |
| ViewModelEventArgs | 42 | — |

**Итого:** 13 файлов, 5,367 строк

---

## 4. DI-контейнер (App.xaml.cs)

**43 зарегистрированных сервиса** (строки 56-191):
- 35 Singleton
- 1 Transient (MainWindow)
- 3 HttpClient-регистрации
- 4 фабричных регистрации

Все координаторы и ViewModels зарегистрированы через интерфейсы.

---

## 5. Тестовое покрытие

**28 файлов *Tests.cs** в `AssetProcessor.Tests/`:

| Категория | Файлов | Примеры |
|-----------|--------|---------|
| Workflow-координаторы | 4 | AssetWorkflow, Connection, Preview, Upload |
| Сервисы | 12 | Download, Histogram, LocalCache, PlayCanvas |
| TextureConversion | 4 | CompressionSettings, HistogramAnalyzer, KtxCreate, MipGenerator |
| UI/Окна | 3 | D3D11Viewer, TextureViewerUI, SettingsWindow |
| Прочие | 5 | B2Upload, SecureSettings, GltfPack |

---

## 6. Достигнутые улучшения производительности

- **SharedProgressState** вместо `Progress<T>` — устранены UI-фризы при загрузке 200+ ассетов
- **Timer-deferred DataGrid loading** — WPF отрисовка разбита на отдельные итерации message pump
- **Silent setters** (`SetIndexSilent`, `SetMasterMaterialNameSilent`) — исключён PropertyChanged-спам при batch-обновлениях
- **Virtualization** на всех DataGrid'ах (Recycling mode)

---

## 7. Оставшийся техдолг

### 7.1 Критические баги (из RELEASE_PREP_AUDIT.md)

| # | Баг | Файл | Severity |
|---|-----|------|----------|
| 1 | Double Dispose `_uploadSemaphore` | B2UploadService.cs:69,639 | CRITICAL |
| 2 | Process leak (нет `using`) | BasisUWrapper.cs:83-119 | CRITICAL |
| 3 | Path Traversal (`../../../`) | LocalCacheService.cs:26-42 | CRITICAL |
| 4 | SemaphoreSlim leak | BasisUWrapper.cs:426-428 | CRITICAL |

### 7.2 Дублирование кода
- **PresetManager + ORMPresetManager**: 400+ LOC дублирования (одинаковый паттерн поиска пресетов)
- DataGrid column loading дублирован в 3 гридах

### 7.3 Dead code и stubs
- 13 пустых LEGACY-stub методов в `MainWindow.TextureViewerUI.cs:307-412`
- 14 `NotImplementedException` в Converters.cs
- 8 кнопок с `// TODO: Implement` в MainWindow.xaml.cs

### 7.4 Race conditions
- `SharedProgressState.Update()` — не атомарное обновление (строки 38-40)
- Fire-and-forget задачи в `ProjectFileWatcherService.cs:113,130`

### 7.5 UI/Service mixing
- `HistogramService` возвращает типы OxyPlot (должен возвращать данные)
- `TextureChannelService` возвращает `BitmapSource` (должен возвращать `byte[]`)

---

## 8. Рекомендации по следующим шагам

### Приоритет 1 — Критические баги
Исправить 4 critical-бага из раздела 7.1 до релиза. Это блокирующие проблемы.

### Приоритет 2 — Покрытие тестами asset-координаторов
- Написать тесты для `AssetLoadCoordinator` (441 строк, 0 тестов)
- Написать тесты для `AssetUploadCoordinator` (473 строки, 0 тестов)
- Расширить тесты `AssetDownloadCoordinator` (164 строки, только 3 теста)

### Приоритет 3 — Очистка dead code
- Удалить 13 stub-методов в TextureViewerUI
- Удалить/реализовать `NotImplementedException` в Converters
- Удалить закомментированный код

### Приоритет 4 — Устранение дублирования
- Объединить PresetManager/ORMPresetManager в единый generic-менеджер
- Унифицировать DataGrid column loading

### Приоритет 5 — Обновить документацию
- Обновить `MVVM_Refactoring_Plan.md` — отметить все 5 фаз как выполненные
- Обновить `RELEASE_PREP_AUDIT.md` по мере исправления багов
