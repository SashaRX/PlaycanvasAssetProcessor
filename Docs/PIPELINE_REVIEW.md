# Pipeline Review - Полная ревизия системы обработки и загрузки ассетов

**Дата**: 2026-01-08
**Версия**: 1.0

---

## 1. Текущее состояние системы

### 1.1 Архитектура пайплайна

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         PLAYCANVAS API                                  │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐                   │
│  │   Textures   │  │    Models    │  │  Materials   │                   │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘                   │
└─────────┼─────────────────┼─────────────────┼───────────────────────────┘
          │                 │                 │
          ▼                 ▼                 ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                      LOCAL PROCESSING                                    │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐                   │
│  │ KTX2 Convert │  │ FBX → GLB    │  │ ORM Packing  │                   │
│  │ + Mipmaps    │  │ + LODs       │  │ (AO+G+M)     │                   │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘                   │
└─────────┼─────────────────┼─────────────────┼───────────────────────────┘
          │                 │                 │
          ▼                 ▼                 ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                         EXPORT PIPELINE                                  │
│  ┌────────────────────────────────────────────────────────────────────┐ │
│  │  server/assets/content/{model}/                                    │ │
│  │  ├── textures/ (*.ktx2)                                            │ │
│  │  ├── {model}.glb, {model}_lod1.glb, ...                           │ │
│  │  ├── materials/{material}.json                                     │ │
│  │  └── {model}.json                                                  │ │
│  └────────────────────────────────────────────────────────────────────┘ │
│  ┌────────────────────────────────────────────────────────────────────┐ │
│  │  server/mapping.json                                               │ │
│  └────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────┘
          │
          ▼ (РАЗРЫВ - нет автоматической связи)
┌─────────────────────────────────────────────────────────────────────────┐
│                      UPLOAD TO B2 CDN                                    │
│  ┌──────────────┐                                                       │
│  │ Manual Only  │  ← Только ручная загрузка текстур                     │
│  │ (Textures)   │  ← Нет загрузки моделей/материалов                    │
│  └──────────────┘  ← Состояние теряется при перезапуске                 │
└─────────────────────────────────────────────────────────────────────────┘
```

### 1.2 Реализованные компоненты

| Компонент | Статус | Описание |
|-----------|--------|----------|
| B2UploadService | ✅ Готов | Полная интеграция B2 API, batch upload, hash verification |
| AssetUploadCoordinator | ✅ Готов | Координация загрузки, проверка hash |
| BaseResource upload props | ✅ Готов | UploadStatus, UploadedHash, RemoteUrl, Progress |
| Settings UI (CDN/Upload) | ✅ Готов | KeyId, ApplicationKey, Bucket, PathPrefix |
| Texture Upload Button | ✅ Готов | Загрузка выбранных/отмеченных текстур |
| Export Pipeline | ✅ Готов | Полный экспорт модели + текстуры + материалы |

### 1.3 Критические проблемы

| # | Проблема | Влияние | Приоритет |
|---|----------|---------|-----------|
| 1 | **Нет persistence состояния загрузки** | Статус теряется при перезапуске | КРИТИЧНО |
| 2 | **Нет загрузки экспортированных моделей** | Модели нельзя деплоить | КРИТИЧНО |
| 3 | **Нет таблицы серверных ассетов** | Непонятно что на сервере | ВЫСОКО |
| 4 | **Export и Upload разорваны** | Требуется ручное вмешательство | ВЫСОКО |
| 5 | **Нет upload history/audit** | Нет истории операций | СРЕДНЕ |
| 6 | **Mapping.json не загружается** | Требуется ручная загрузка | СРЕДНЕ |

---

## 2. Неиспользуемый/устаревший код

### 2.1 Файлы для удаления

| Файл | Причина |
|------|---------|
| `Services/PlayCanvasServiceBase.cs` | Устарел, есть полный `PlayCanvasService` |
| `Controls/TextureConversionSettingsPanel.xaml.cs.backup` | Backup файл |

### 2.2 Дублирующийся код

| Файл | Проблема |
|------|----------|
| `Helpers/Helpers.cs` | `OpenFileStreamWithRetryAsync` и `OpenFileWithRetryAsync` - дублируют логику |

### 2.3 Неиспользуемые компоненты

| Файл | Статус | Рекомендация |
|------|--------|--------------|
| `ViewModels/ModelConversionViewModel.cs` | Не подключен к UI | Интегрировать или удалить |
| `ModelConversion/` директория | Частично используется | Провести аудит |

---

## 3. Документация CLAUDE.md - что нужно добавить

### 3.1 Отсутствующие разделы

1. **CDN/Upload Pipeline** - нет документации по B2 upload
2. **Model Export Pipeline** - описан частично
3. **Server Asset Structure** - нет описания структуры `server/`
4. **Mapping.json format** - нет спецификации

### 3.2 Устаревшая информация

- Нет упоминания B2UploadService, AssetUploadCoordinator
- Нет описания upload status tracking в BaseResource
- Нет секции про CDN settings

---

## 4. План улучшений

### Фаза 1: Критические исправления (Приоритет: КРИТИЧНО)

#### 1.1 Persistent Upload State
**Цель**: Сохранять состояние загрузки между сессиями

```
Новые файлы:
- Data/UploadStateDatabase.cs (SQLite wrapper)
- Data/UploadRecord.cs (модель записи)
- Services/IUploadStateService.cs
- Services/UploadStateService.cs

Структура БД:
CREATE TABLE upload_history (
    id INTEGER PRIMARY KEY,
    local_path TEXT NOT NULL,
    remote_path TEXT NOT NULL,
    content_sha1 TEXT NOT NULL,
    content_length INTEGER,
    uploaded_at DATETIME,
    cdn_url TEXT,
    status TEXT,
    error_message TEXT
);
```

#### 1.2 Model Export Upload
**Цель**: Кнопка загрузки после экспорта модели

```
UI изменения:
- MainWindow.xaml: Add "Upload Export" button in ModelExportGroupBox
- MainWindow.Models.cs: UploadExportButton_Click handler

Логика:
1. После успешного экспорта → показать кнопку "Upload to CDN"
2. Загрузить все файлы из export directory
3. Автоматически загрузить mapping.json
4. Показать progress и результат
```

### Фаза 2: Server Asset Tracking (Приоритет: ВЫСОКО)

#### 2.1 Server Assets Table
**Цель**: Таблица с содержимым CDN

```
Новые файлы:
- Controls/ServerAssetsPanel.xaml
- Controls/ServerAssetsPanel.xaml.cs
- ViewModels/ServerAssetViewModel.cs

UI:
- Новая вкладка "Server" в MainWindow
- DataGrid с колонками: Path, Size, UploadedAt, SHA1, CDN URL
- Кнопки: Refresh, Delete, Open URL, Compare with Local

Функции:
- B2UploadService.ListFilesAsync() → заполнение таблицы
- Сравнение с локальными файлами (hash comparison)
- Подсветка: "Only on Server", "Only Local", "Hash Mismatch"
```

#### 2.2 Unified Asset Status Column
**Цель**: Единая колонка статуса в таблицах текстур/моделей

```
Статусы:
- "Not Exported" (серый)
- "Exported" (синий)
- "Uploading..." (желтый + progress)
- "Uploaded" (зеленый) + CDN URL tooltip
- "Upload Failed" (красный)
- "Outdated" (оранжевый) - локальный hash != серверный
```

### Фаза 3: Workflow Integration (Приоритет: ВЫСОКО)

#### 3.1 Export → Upload Workflow
**Цель**: Автоматическая загрузка после экспорта

```
ExportOptions изменения:
+ AutoUploadAfterExport: bool
+ UploadMappingJson: bool

Workflow:
1. Export Model → создаются файлы
2. If AutoUploadAfterExport:
   a. Upload all exported files
   b. Update UploadState database
   c. Upload mapping.json
3. Show summary dialog
```

#### 3.2 Batch Operations
**Цель**: Массовые операции над ассетами

```
Операции:
- "Upload All Exported" - загрузить все экспортированные
- "Sync to Server" - загрузить только измененные
- "Verify Server" - проверить целостность на сервере
```

### Фаза 4: UX Improvements (Приоритет: СРЕДНЕ)

#### 4.1 Upload Progress Enhancement
```
Улучшения:
- ETA (estimated time remaining)
- Upload speed (MB/s)
- Individual file progress в tooltip
- Retry button для failed uploads
```

#### 4.2 CDN URL Management
```
Функции:
- Copy CDN URL to clipboard
- Open CDN URL in browser
- Generate embed code (for PlayCanvas)
```

### Фаза 5: Audit & History (Приоритет: СРЕДНЕ)

#### 5.1 Upload History Viewer
```
Новые файлы:
- Windows/UploadHistoryWindow.xaml
- Windows/UploadHistoryWindow.xaml.cs

Функции:
- Просмотр всех загрузок
- Фильтрация по дате/статусу
- Export to CSV
- Re-upload selected
```

---

## 5. Рекомендуемый порядок реализации

### Итерация 1 (Критично)
1. ✅ Создать UploadStateService с SQLite persistence
2. ✅ Добавить кнопку "Upload Export" для моделей
3. ✅ Автоматическая загрузка mapping.json

### Итерация 2 (Server Tracking)
4. ➡️ Добавить вкладку "Server" с таблицей ассетов
5. ➡️ Реализовать сравнение local vs server
6. ➡️ Добавить колонку статуса в DataGrid

### Итерация 3 (Workflow)
7. ➡️ Опция AutoUploadAfterExport
8. ➡️ Batch sync operations
9. ➡️ Upload history viewer

### Итерация 4 (Polish)
10. ➡️ Enhanced progress UI
11. ➡️ CDN URL management
12. ➡️ Audit logging

---

## 6. Структура файлов после улучшений

```
AssetProcessor/
├── Data/
│   ├── UploadStateDatabase.cs      # NEW: SQLite wrapper
│   └── UploadRecord.cs             # NEW: Upload history record
├── Services/
│   ├── IUploadStateService.cs      # NEW: Interface
│   ├── UploadStateService.cs       # NEW: Implementation
│   ├── AssetUploadCoordinator.cs   # MODIFY: Use state service
│   └── B2UploadService.cs          # EXISTS
├── Controls/
│   ├── ServerAssetsPanel.xaml      # NEW: Server assets view
│   └── ServerAssetsPanel.xaml.cs   # NEW: Code-behind
├── Windows/
│   └── UploadHistoryWindow.xaml    # NEW: History viewer
├── ViewModels/
│   └── ServerAssetViewModel.cs     # NEW: Server asset VM
└── MainWindow.xaml.cs              # MODIFY: Add upload handlers
```

---

## 7. Удаление устаревшего кода

```bash
# Файлы для удаления:
rm Services/PlayCanvasServiceBase.cs
rm Controls/TextureConversionSettingsPanel.xaml.cs.backup

# Рефакторинг:
# Helpers/Helpers.cs - объединить дублирующиеся методы
```

---

## 8. Обновление CLAUDE.md

Добавить секции:
1. **CDN/Upload Pipeline** - полное описание B2 upload workflow
2. **Export Pipeline** - детали ModelExportPipeline
3. **Server Asset Structure** - описание структуры `server/`
4. **Upload State Tracking** - как работает persistence

Обновить секции:
1. **Resource Models** - добавить upload properties
2. **Important File Locations** - добавить Upload/, Data/
3. **Common Workflows** - добавить "Deploying to CDN"

---

## Заключение

Текущая система имеет хорошую основу (B2UploadService, Export Pipeline), но страдает от:
1. Отсутствия persistence - критическая проблема
2. Разрыва между Export и Upload - требует ручного вмешательства
3. Отсутствия видимости серверных ассетов - пользователь "слепой"

Рекомендуется начать с Фазы 1 (persistence + model upload), затем Фаза 2 (server tracking).
