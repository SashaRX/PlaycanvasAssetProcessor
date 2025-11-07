# ModelConversion Pipeline

Офлайн-конвейер для конвертации FBX моделей в оптимизированные GLB с автоматической генерацией LOD (Level of Detail) цепочки.

## Обзор

ModelConversion - это комплексный пайплайн для обработки 3D моделей, ориентированный на веб-рантайм (PlayCanvas) и офлайн-проверку в редакторах. Пайплайн автоматизирует следующие задачи:

1. **Конвертация FBX → GLB**: Стабильная конвертация исходных FBX в glTF 2.0 бинарный формат
2. **Генерация LOD цепочки**: Автоматическое создание 4 уровней детализации (LOD0-LOD3)
3. **Оптимизация геометрии**: Упрощение сетки с сохранением визуального качества
4. **Сжатие**: Квантование и опциональная EXT_meshopt_compression для минимальных размеров
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

### 1. FBX2glTF

Конвертирует FBX в glTF/GLB.

**Установка (Windows):**
```bash
# Скачать с GitHub releases
# https://github.com/godotengine/FBX2glTF/releases

# Или использовать предкомпилированный бинарник
# Поместить FBX2glTF-windows-x64.exe в PATH или указать путь в настройках
```

**Проверка:**
```bash
FBX2glTF-windows-x64.exe --help
```

### 2. gltfpack (meshoptimizer)

Оптимизирует GLB: упрощение, квантование, EXT_meshopt_compression.

**Установка (Windows):**
```bash
# Скачать gltfpack с meshoptimizer releases
# https://github.com/zeux/meshoptimizer/releases

# Или установить через npm (требует Node.js)
npm install -g gltfpack

# Поместить gltfpack.exe в PATH или указать путь в настройках
```

**Проверка:**
```bash
gltfpack -h
```

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
   - Максимальное сжатие для web runtime
   - Требует meshopt декодер в браузере
4. **MeshOptAggressive**: EXT_meshopt_compression с дополнительным сжатием
   - Флаг gltfpack: `-cc`
   - Ещё меньший размер, чуть медленнее декодирование

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

## Использование

### Базовый пример

```csharp
using AssetProcessor.ModelConversion.Core;
using AssetProcessor.ModelConversion.Pipeline;

// Создаём пайплайн
var pipeline = new ModelConversionPipeline(
    fbx2glTFPath: @"C:\Tools\FBX2glTF-windows-x64.exe",
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
3. **Текстуры не обрабатываются**: Используйте внешний KTX2-пайплайн для текстур
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
