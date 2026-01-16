# ModelConversion Pipeline

Офлайн-конвейер для конвертации FBX/glTF/GLB моделей в оптимизированные GLB с автоматической генерацией LOD (Level of Detail) цепочки.

## Обзор

ModelConversion - это комплексный пайплайн для обработки 3D моделей, ориентированный на веб-рантайм (PlayCanvas) и офлайн-проверку в редакторах. Пайплайн автоматизирует следующие задачи:

1. **Конвертация FBX/glTF → GLB**: Конвертация исходных FBX или прямой glTF/GLB вход в оптимизированный формат
2. **Генерация LOD цепочки**: Автоматическое создание 4 уровней детализации (LOD0-LOD3)
3. **Оптимизация геометрии**: Упрощение сетки с сохранением визуального качества
4. **Сжатие**: Квантование или EXT_meshopt_compression
5. **QA отчёты**: Автоматическая валидация результатов по критериям приёмки

## Архитектура

```
ModelConversion/
├── Core/                       # Базовые типы и настройки
│   ├── LodLevel.cs            # Enum и настройки LOD уровней
│   ├── CompressionMode.cs     # Режимы сжатия (None, Quantization, MeshOpt)
│   └── ModelConversionSettings.cs  # Главные настройки конвертации
├── Wrappers/                  # CLI обёртки для внешних инструментов
│   ├── FBX2glTFWrapper.cs     # Обёртка для FBX2glTF
│   └── GltfPackWrapper.cs     # Обёртка для gltfpack (meshoptimizer)
├── Pipeline/                  # Основной пайплайн
│   ├── ModelConversionPipeline.cs   # Главный оркестратор
│   └── LodManifestGenerator.cs      # Генератор JSON манифестов
└── Analysis/                  # Метрики и QA
    ├── MeshMetrics.cs         # Метрики геометрии (треугольники, размер)
    └── QualityReport.cs       # QA отчёт с критериями приёмки
```

## Установка инструментов

Пайплайн требует следующие CLI инструменты:

### 1. FBX2glTF (Godot версия - рекомендуется)

Конвертирует FBX в glTF/GLB. **Рекомендуется использовать Godot fork** FBX2glTF, так как он активно поддерживается и имеет улучшения по сравнению с оригинальной версией.

**Установка (Windows):**
```bash
# Скачать Godot FBX2glTF с GitHub releases
# https://github.com/godotengine/FBX2glTF/releases

# Или оригинальную версию (устаревшая)
# https://github.com/facebookincubator/FBX2glTF/releases

# Поместить FBX2glTF-windows-x86_64.exe в удобное место
# Путь можно указать в GUI через кнопку "Browse..."
# При сборке проекта FBX2glTF скачивается автоматически в папку Tools/
```

**Проверка:**
```bash
FBX2glTF-windows-x86_64.exe --help
```

**Примечание:** Godot FBX2glTF совместим с оригинальной версией по параметрам командной строки.

### 2. gltfpack (meshoptimizer 1.0)

Оптимизирует GLB: упрощение, квантование, EXT_meshopt_compression. Начиная с meshoptimizer **1.0** gltfpack изменил значения по умолчанию и формат выходных данных (расширенная поддержка KHR_mesh_quantization, актуализированные флаги), поэтому рекомендуем использовать именно релиз 1.0+.

**Установка (Windows):**
```bash
# Скачать gltfpack с meshoptimizer releases (1.0)
# https://github.com/zeux/meshoptimizer/releases/tag/v1.0

# Или установить через npm (требует Node.js)
npm install -g gltfpack

# Поместить gltfpack.exe в PATH или указать путь в настройках
```

**Проверка версии:**
```bash
gltfpack -v
```

Утилита выводит номер версии при запуске без других аргументов; если отображается < 1.0, обновите бинарник, чтобы избежать несовместимых флагов и формата EXT_meshopt_compression.

**Совместимость флагов (meshoptimizer 1.0):**

- `-c` / `-cc` (и `-cf` для fallback): EXT_meshopt_compression остаётся, но в 1.0 изменён байткод сжатия — в рантайме нужен декодер 1.0+, иначе возможны артефакты. При миграции с 0.15+ перепроверьте качество/размер и используйте `-cf`, если нужно оставить совместимость.
- `-kv`: По умолчанию 1.0 агрессивно удаляет неиспользуемые атрибуты; включайте `-kv`, если требуется сохранить точный набор вершинных атрибутов как в 0.15+.
- `-ke` / `-km` / `-kn`: Флаги сохранения extras/материалов/именованных узлов. В 1.0 оптимизации по умолчанию могут сливать материалы и узлы, поэтому для повторяемого экспорта добавляйте эти флаги в пресеты явно.

**Рантайм требования:** Для корректного воспроизведения EXT_meshopt_compression необходим **MeshoptDecoder 1.0** (или новее) в браузере/движке, иначе декодирование может выдавать артефакты из-за несовпадения формата байт-кода в 1.0.

## Экспорт из 3ds Max

### Прямой glTF экспорт (рекомендуется для 3ds Max 2025+)

3ds Max 2025 поддерживает нативный экспорт в glTF. Это предпочтительный способ, так как исключает промежуточное преобразование через FBX.

**File → Export → glTF (.gltf/.glb)**

**Рекомендуемые настройки:**

| Параметр | Значение | Примечание |
|----------|----------|------------|
| Format | GLB (Binary) | Один файл, проще в работе |
| Embed Textures | OFF | Текстуры обрабатываются отдельно через TextureConversion |
| Include Geometry | ON | - |
| Include Materials | ON | Только параметры, не текстуры |
| Include Cameras | OFF | Не нужно для моделей |
| Include Lights | OFF | Не нужно для моделей |

### FBX экспорт (альтернатива)

Если glTF экспорт недоступен, используйте FBX с последующей конвертацией через FBX2glTF.

**Рекомендуемые настройки FBX Export:**

| Параметр | Значение | Примечание |
|----------|----------|------------|
| **Axis Conversion** | **Y-Up** | glTF использует Y-up, 3ds Max — Z-up |
| Units | Automatic / Meters | glTF стандарт — метры |
| Scale Factor | 1.0 | Без масштабирования |
| Triangulate | ON | Избегаем N-gons |
| Preserve Edge Orientation | ON | Сохраняем нормали |
| Skin/Bone influences | ≤ 4 | Лимит glTF 2.0 |

**Важно про Axis Conversion:**
- 3ds Max использует **Z-up** (нельзя изменить внутри программы)
- glTF стандарт использует **Y-up**
- При экспорте FBX выберите **Y-Up** для автоматической конвертации
- Конвертация применяется только к корневым объектам
- Если на корневом объекте есть анимация, кривые будут ресемплированы

### Прямой glTF/GLB вход в пайплайн

Пайплайн автоматически определяет формат входного файла:

```csharp
// FBX вход — проходит через FBX2glTF
var result = await pipeline.ConvertAsync("model.fbx", outputDir, settings);

// glTF/GLB вход — напрямую в gltfpack (пропускает FBX2glTF)
var result = await pipeline.ConvertAsync("model.glb", outputDir, settings);
var result = await pipeline.ConvertAsync("model.gltf", outputDir, settings);
```

При glTF/GLB входе:
- FBX2glTF **не требуется** (можно не указывать путь)
- Файл передаётся напрямую в gltfpack для оптимизации и LOD генерации
- Поддерживаются оба формата: `.gltf` (JSON + bin) и `.glb` (binary)

## Режимы сжатия

| Режим | Флаги gltfpack | Описание |
|-------|----------------|----------|
| `None` | - | Без сжатия (только упрощение геометрии) |
| `Quantization` | `-kn -km` | KHR_mesh_quantization (совместимо с редакторами) |
| `MeshOpt` | `-c` | EXT_meshopt_compression (рекомендуется для web) |
| `MeshOptAggressive` | `-cc` | EXT_meshopt_compression + дополнительное сжатие |

**Рекомендации:**
- **Quantization** — универсальная совместимость, подходит для редакторов
- **MeshOpt** — рекомендуется для PlayCanvas, Three.js, Babylon.js
- **MeshOptAggressive** — минимальный размер, чуть медленнее декодирование

## Использование GUI

### Открытие окна конвертации моделей

1. Запустите TexTool (PlayCanvas Asset Processor)
2. Откройте меню **Tools → Model Conversion Pipeline (FBX → GLB + LOD)...**
3. Откроется окно `ModelConversionWindow`

### Настройка путей к инструментам

В верхней панели окна конвертации:

1. **FBX2glTF**: Укажите путь к `FBX2glTF-windows-x86_64.exe`
   - Введите путь вручную или нажмите **Browse...** для выбора файла
   - Рекомендуется: Godot FBX2glTF (см. раздел "Установка инструментов")

2. **gltfpack**: Укажите путь к `gltfpack.exe`
   - Введите путь вручную или нажмите **Browse...** для выбора файла
   - Если установлен через npm, обычно находится в `%AppData%\npm\gltfpack.exe`

3. **Output Directory**: Директория для выходных файлов
   - По умолчанию: `output_models`
   - Нажмите **Browse...** для выбора другой директории

**Примечание:** Пути сохраняются автоматически и загружаются при следующем запуске.

### Добавление и обработка моделей

1. **Добавить модели**: Нажмите "Add FBX Models" и выберите один или несколько FBX файлов
2. **Настройка параметров**: Выберите модель в списке и настройте параметры справа:
   - Quick Presets: Default, Production, HighQuality, MinSize
   - Model Settings: режим сжатия, генерация LOD, манифесты, QA отчёты
   - Quantization Settings: биты квантования для позиций, UV, нормалей, цветов
   - LOD Chain Configuration: детальная настройка каждого LOD уровня
3. **Обработка**:
   - "Process Selected" - обработать выбранную модель
   - "Process All Enabled" - обработать все включенные модели (галочка ✓)
4. **Сохранение настроек**: Нажмите "Save Settings" для сохранения конфигурации

### Пресеты

Быстрые пресеты для применения к выбранной модели или всем моделям:

- **Default**: Quantization, 4 LOD уровня, манифест + QA отчёт
- **Production**: MeshOpt (EXT_meshopt_compression), два трека (glb + meshopt), агрессивное упрощение
- **HighQuality**: Quantization с высокими битами, мягкое упрощение LOD
- **MinSize**: Aggressive MeshOpt, минимальные размеры файлов

## Настройки конвертации

### Уровни LOD

По умолчанию генерируется 4 уровня детализации:

| LOD   | Упрощение | Агрессивность | Порог переключения (screen coverage) |
|-------|-----------|---------------|--------------------------------------|
| LOD0  | 100%      | Нет           | 0.25 (25% экрана)                    |
| LOD1  | 60%       | Да            | 0.10 (10% экрана)                    |
| LOD2  | 30%       | Да            | 0.04 (4% экрана)                     |
| LOD3  | 12%       | Да            | 0.02 (2% экрана)                     |

Пороги переключения можно настроить через `LodSettings.SwitchThreshold`.

### Режимы сжатия

1. **None**: Без сжатия (только упрощение геометрии)
2. **Quantization**: KHR_mesh_quantization (совместимо с редакторами)
   - Флаги gltfpack: `-kn -km`
   - Уменьшает размер без потери совместимости
3. **MeshOpt**: EXT_meshopt_compression
   - Флаг gltfpack: `-c`
   - Рекомендуется для web runtime (PlayCanvas, Three.js, Babylon.js)
   - Требует meshopt декодер в браузере
4. **MeshOptAggressive**: EXT_meshopt_compression с дополнительным сжатием
   - Флаг gltfpack: `-cc`
   - Минимальный размер, чуть медленнее декодирование

### Квантование вершин

Настройки квантования (биты на компонент):

```csharp
var quantization = new QuantizationSettings {
    PositionBits = 14,   // Позиции вершин (1-16, default 14)
    TexCoordBits = 12,   // UV координаты (1-16, default 12)
    NormalBits = 8,      // Нормали (1-16, default 8)
    ColorBits = 8        // Цвета вершин (1-16, default 8)
};
```

**Пресеты:**
- `CreateDefault()`: Баланс качества/размера (14/12/8/8)
- `CreateHighQuality()`: Максимальное качество (16/14/10/10)
- `CreateMinSize()`: Минимальный размер (12/10/8/8)

### Исключение текстур (Exclude Textures)

**ВАЖНО:** По умолчанию текстуры **ИСКЛЮЧАЮТСЯ** из GLB файлов.

Текстуры должны обрабатываться отдельно через **TextureConversion пайплайн** для оптимальной компрессии (Basis Universal, KTX2).

```csharp
var settings = new ModelConversionSettings {
    ExcludeTextures = true,  // По умолчанию true
    // ...
};
```

**Почему важно исключать текстуры:**

1. **Размер файлов**: Текстуры могут увеличить GLB в 10-20 раз
   - Пример: FBX 379 KB → GLB с текстурами 6.3 MB → GLB без текстур ~400 KB

2. **LOD эффективность**: Упрощение геометрии не уменьшает размер текстур
   - Все LOD уровни будут содержать одинаковые текстуры → одинаковый размер файлов

3. **Оптимизация текстур**: TextureConversion пайплайн предоставляет:
   - Basis Universal компрессию (ETC1S/UASTC)
   - Автоматическую генерацию мипмапов
   - Квантование и нормализацию
   - KTX2 формат с метаданными

**Метод исключения текстур:**

1. **FBX2glTF** (`--binary --separate-textures`):
   - Создаёт GLB файл, но текстуры НЕ встраиваются
   - Текстуры остаются как внешние файлы (.png/.jpg)

2. **gltfpack** (`-tr`):
   - Флаг `-tr`: keep referring to original texture paths instead of copying/embedding images
   - Предотвращает встраивание внешних текстур в оптимизированный GLB
   - Текстуры остаются как внешние файлы

**GUI настройка:**
- Checkbox "Exclude Textures" в разделе Model Settings (по умолчанию включен)

**Рекомендация:** Всегда используйте `ExcludeTextures = true` и обрабатывайте текстуры через TextureConversion пайплайн отдельно.

## Использование

### Базовый пример

```csharp
using AssetProcessor.ModelConversion.Core;
using AssetProcessor.ModelConversion.Pipeline;

// Создаём пайплайн
var pipeline = new ModelConversionPipeline(
    fbx2glTFPath: @"C:\Tools\FBX2glTF-windows-x86_64.exe",
    gltfPackPath: @"C:\Tools\gltfpack.exe"
);

// Настройки по умолчанию (Quantization only)
var settings = ModelConversionSettings.CreateDefault();

// Конвертируем
var result = await pipeline.ConvertAsync(
    inputFbxPath: @"C:\Models\MyModel.fbx",
    outputDirectory: @"C:\Output",
    settings: settings
);

if (result.Success) {
    Console.WriteLine($"Conversion completed in {result.Duration.TotalSeconds:F2}s");
    Console.WriteLine($"LOD files: {result.LodFiles.Count}");
    Console.WriteLine($"Manifest: {result.ManifestPath}");
    Console.WriteLine($"QA Report: {result.QAReportPath}");
}
```

### Продакшн сборка (EXT_meshopt_compression)

```csharp
// Генерирует два трека:
// 1. dist/glb - только квантование (fallback для редакторов)
// 2. dist/meshopt - с EXT_meshopt_compression (для продакшена)
var settings = ModelConversionSettings.CreateProduction();

var result = await pipeline.ConvertAsync(
    inputFbxPath: @"C:\Models\MyModel.fbx",
    outputDirectory: @"C:\Output",
    settings: settings
);
```

### Минимальный размер

```csharp
var settings = ModelConversionSettings.CreateMinSize();
// Использует MeshOptAggressive + минимальное квантование

var result = await pipeline.ConvertAsync(
    inputFbxPath: @"C:\Models\MyModel.fbx",
    outputDirectory: @"C:\Output",
    settings: settings
);
```

### Кастомные LOD настройки

```csharp
var settings = new ModelConversionSettings {
    GenerateLods = true,
    LodChain = new List<LodSettings> {
        LodSettings.CreateDefault(LodLevel.LOD0),
        new LodSettings {
            Level = LodLevel.LOD1,
            SimplificationRatio = 0.7f,  // Более мягкое упрощение
            AggressiveSimplification = false,
            SwitchThreshold = 0.15f
        },
        // ... другие LOD
    },
    CompressionMode = CompressionMode.MeshOpt,
    Quantization = QuantizationSettings.CreateHighQuality()
};
```

## Структура выходных файлов

После успешной конвертации создаётся следующая структура:

```
output/
├── dist/
│   ├── glb/                    # Fallback трек (Quantization only)
│   │   ├── MyModel_lod0.glb
│   │   ├── MyModel_lod1.glb
│   │   ├── MyModel_lod2.glb
│   │   └── MyModel_lod3.glb
│   ├── meshopt/                # Продакшн трек (EXT_meshopt_compression)
│   │   ├── MyModel_lod0.glb
│   │   ├── MyModel_lod1.glb
│   │   ├── MyModel_lod2.glb
│   │   └── MyModel_lod3.glb
│   └── manifest/
│       └── lod-index.json      # Манифест для рантайм-лоадера
└── reports/
    ├── MyModel_glb_report.json
    └── MyModel_meshopt_report.json
```

## JSON Манифест

Манифест используется рантайм-лоадером для загрузки правильных LOD уровней:

```json
{
  "MyModel": {
    "lods": [
      { "url": "../meshopt/MyModel_lod0.glb", "switchThreshold": 0.25 },
      { "url": "../meshopt/MyModel_lod1.glb", "switchThreshold": 0.10 },
      { "url": "../meshopt/MyModel_lod2.glb", "switchThreshold": 0.04 },
      { "url": "../meshopt/MyModel_lod3.glb", "switchThreshold": 0.02 }
    ],
    "hysteresis": 0.02
  }
}
```

- `switchThreshold`: Доля экрана (0.0-1.0) при которой происходит переключение
- `hysteresis`: Предотвращает "мерцание" при переключении LOD

## QA Отчёт

Автоматически генерируемый отчёт содержит:

1. **Метрики геометрии**: Треугольники, вершины, размер файла, bbox
2. **Критерии приёмки**:
   - LOD Chain Generated: Все 4 LOD уровня присутствуют
   - LOD1 Size Reduction: LOD1 размер ≤ 60% от LOD0
   - Triangle Count: Соответствие целевым коэффициентам упрощения
   - Bounding Box Consistency: Консистентность bbox между LOD
   - Output Files Exist: Все GLB файлы сгенерированы

3. **Warnings/Errors**: Список предупреждений и ошибок

Пример:

```
=== QA REPORT: MyModel ===
Generated: 2025-11-07 15:30:00

LOD Metrics:
  LOD0:
    Triangles: 10,000
    Vertices: 5,500
    File Size: 245,000 bytes (239.26 KB)
    BBox: [-5.00, -2.00, -3.00] - [5.00, 2.00, 3.00]
  LOD1:
    Triangles: 6,000
    Vertices: 3,300
    File Size: 140,000 bytes (136.72 KB)

Acceptance Criteria:
  ✓ PASS: LOD Chain Generated - All 4 LOD levels present
  ✓ PASS: LOD1 Size Reduction - LOD1 is 57.1% of LOD0 size (OK)
  ✓ PASS: LOD1 Triangle Count - 6000 tris (60.0% of LOD0), expected ~60.0%
  ...

Overall Result: ✓ PASSED
```

## Runtime интеграция (PlayCanvas)

### 1. Подключить meshopt декодер

Для чтения EXT_meshopt_compression в PlayCanvas:

```javascript
// Загружаем meshopt декодер
import { MeshoptDecoder } from 'meshoptimizer';

// Инициализируем
await MeshoptDecoder.ready;
app.loader.getHandler('container').decoders.meshopt = MeshoptDecoder;
```

### 2. LOD система

```javascript
// Загружаем манифест
const manifest = await fetch('dist/manifest/lod-index.json').then(r => r.json());

// LOD менеджер
class LodManager {
    constructor(modelName, manifest) {
        this.lods = manifest[modelName].lods;
        this.hysteresis = manifest[modelName].hysteresis;
        this.currentLod = 0;
    }

    update(camera, entity) {
        // Вычисляем screen coverage
        const coverage = this.calculateScreenCoverage(camera, entity);

        // Переключаем LOD с гистерезисом
        for (let i = 0; i < this.lods.length; i++) {
            const threshold = this.lods[i].switchThreshold;
            if (coverage >= threshold * (1 - this.hysteresis)) {
                if (this.currentLod !== i) {
                    this.switchLod(i);
                }
                break;
            }
        }
    }

    calculateScreenCoverage(camera, entity) {
        // Реализация расчёта покрытия экрана
        // На основе bbox и расстояния до камеры
    }

    switchLod(lodIndex) {
        this.currentLod = lodIndex;
        // Загружаем GLB и заменяем модель
    }
}
```

## Лучшие практики

### 1. Оптимизация источников

Перед конвертацией убедитесь что исходные FBX:
- Триангулированы (не содержат N-gons)
- Очищены от мусора (пустые ноды, неиспользуемые материалы)
- Имеют правильный unit scale (метры) и Y-up ориентацию
- Имеют валидные UV координаты

### 2. Выбор режима сжатия

- **Quantization**: Для редакторов (Unity, Godot, Blender)
- **MeshOpt**: Для веб-продакшна (PlayCanvas, Three.js 122+, Babylon.js 5.0+)
- **GenerateBothTracks**: Когда нужны оба варианта

### 3. Настройка LOD порогов

Для мелко-модульной геометрии (решётки, проволока):
- Используйте более мягкие коэффициенты упрощения для LOD2/LOD3
- Или создайте исключения (skip LOD generation для таких моделей)

Для крупных объектов (здания, ландшафт):
- Можно использовать более агрессивные LOD3 (si 0.08 вместо 0.12)

### 4. Проверка качества

После конвертации проверьте QA отчёт:
- Все критерии должны быть PASSED
- LOD2/LOD3 не должны иметь видимых дыр в UV
- Bounding box консистентен между LOD

Если LOD1 даёт сильную деградацию швов/нормалей:
- Увеличьте `-si` на 0.7 вместо 0.6
- Или отключите `-sa` (агрессивное упрощение) для LOD1

## Ограничения

1. **Не смешиваем Draco и EXT_meshopt**: Используйте только один метод сжатия
2. **Не используем MSFT_lod**: Не вкладываем LOD в один GLB (плохая поддержка в экосистеме)
3. **Текстуры исключены по умолчанию**: Используйте TextureConversion пайплайн для обработки текстур отдельно
4. **Только для статичных мешей**: Анимации и скелеты требуют дополнительной обработки

## Troubleshooting

### FBX2glTF fails with "FBX SDK error"

- Убедитесь что FBX файл валидный (откройте в Autodesk FBX Review)
- Проверьте что FBX триангулирован
- Удалите пустые ноды и неиспользуемые материалы

### gltfpack generates broken LOD2/LOD3

- Уменьшите агрессивность упрощения: `SimplificationRatio = 0.4f` для LOD2
- Отключите `-sa` для LOD1/LOD2
- Проверьте что исходная геометрия топологически правильная

### LOD1 size > 60% of LOD0

- Проверьте что gltfpack установлен корректно
- Убедитесь что `-c` флаг работает (требует meshoptimizer 0.15+)
- Попробуйте `-cc` для более агрессивного сжатия

### PlayCanvas не загружает EXT_meshopt_compression

- Убедитесь что подключён meshopt декодер
- Проверьте версию PlayCanvas Engine (требуется поддержка EXT_meshopt_compression)
- Используйте fallback трек (dist/glb) для тестирования

## Дополнительные ресурсы

- [FBX2glTF GitHub](https://github.com/godotengine/FBX2glTF)
- [meshoptimizer GitHub](https://github.com/zeux/meshoptimizer)
- [gltfpack Documentation](https://meshoptimizer.org/gltf/)
- [EXT_meshopt_compression Spec](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Vendor/EXT_meshopt_compression)
- [PlayCanvas Engine Docs](https://developer.playcanvas.com/)
