# ORM Channel Packing для glTF/PlayCanvas

## Обзор

Система упаковки ORM (Occlusion-Roughness-Metalness) позволяет объединять несколько монохромных текстур PBR-материалов в единую RGBA текстуру для оптимизации количества текстур в glTF/PlayCanvas проектах.

## Режимы упаковки

### 1. OG (2 канала)
Для non-metallic материалов:
- **RGB** = Ambient Occlusion (AO)
- **A** = Gloss

### 2. OGM (3 канала)
Стандартная упаковка:
- **R** = Ambient Occlusion (AO)
- **G** = Gloss
- **B** = Metallic

### 3. OGMH (4 канала)
Полная упаковка с высотой:
- **R** = Ambient Occlusion (AO)
- **G** = Gloss
- **B** = Metallic
- **A** = Height/Mask

## Основные возможности

### Автоматическое обнаружение текстур

Система автоматически находит исходные текстуры по шаблонам имен:
- `_ao`, `_AO`, `_ambientocclusion`, `_occlusion`
- `_gloss`, `_Gloss`, `_glossiness`, `_smoothness`
- `_metallic`, `_Metallic`, `_metalness`, `_metal`
- `_height`, `_Height`, `_displacement`, `_disp`

```csharp
var detector = new ORMTextureDetector();
var detection = detector.DetectORMTextures("material_albedo.png");
var packingSettings = detector.CreateSettingsFromDetection("material_albedo.png");
```

### Специальная обработка каналов

#### AO Processing
Два режима для сохранения деталей затенения в мипмапах:

1. **BiasedDarkening** (рекомендуется):
   ```csharp
   channelSettings.AOProcessingMode = AOProcessingMode.BiasedDarkening;
   channelSettings.AOBias = 0.5f; // 0.0 = светлее, 1.0 = темнее
   ```
   Формула: `lerp(mean, min, bias)`
   - `bias = 0.3` - более светлые мипы (сохраняет яркие области)
   - `bias = 0.5` - средний баланс
   - `bias = 0.7` - более темные мипы (усиливает тени)

2. **Percentile**:
   ```csharp
   channelSettings.AOProcessingMode = AOProcessingMode.Percentile;
   channelSettings.AOPercentile = 10.0f; // 10-й перцентиль
   ```

#### Toksvig для Gloss

Автоматическая коррекция глянца на основе дисперсии normal map:

```csharp
channelSettings.ApplyToksvig = true;
channelSettings.ToksvigSettings = new ToksvigSettings {
    Enabled = true,
    CompositePower = 4.0f, // 1-8, выше = сильнее эффект
    CalculationMode = ToksvigCalculationMode.Simplified
};
```

### Профили фильтрации

Каждый канал использует оптимальный фильтр:
- **AO**: Kaiser filter (высокое качество)
- **Gloss**: Kaiser filter + Toksvig correction
- **Metallic**: Box filter (для бинарных значений)
- **Height**: Kaiser filter

## Примеры использования

### Пример 1: Автоматическая упаковка

```csharp
var detector = new ORMTextureDetector();
var packingSettings = detector.CreateSettingsFromDetection("material_albedo.png");

// Настройка AO
packingSettings.RedChannel.AOProcessingMode = AOProcessingMode.BiasedDarkening;
packingSettings.RedChannel.AOBias = 0.5f;

// Настройка Gloss
packingSettings.GreenChannel.ApplyToksvig = true;
packingSettings.GreenChannel.ToksvigSettings = ToksvigSettings.CreateDefault();

// Компрессия
var pipeline = new TextureConversionPipeline();
var compressionSettings = CompressionSettings.CreateETC1SDefault();
compressionSettings.QualityLevel = 192;
compressionSettings.ColorSpace = ColorSpace.Linear; // КРИТИЧНО!

var result = await pipeline.ConvertPackedTextureAsync(
    packingSettings,
    "material_orm.ktx2",
    compressionSettings
);
```

### Пример 2: Ручная настройка OGMH

```csharp
var packingSettings = ChannelPackingSettings.CreateDefault(ChannelPackingMode.OGMH);

// R = AO
packingSettings.RedChannel.SourcePath = "rock_ao.png";
packingSettings.RedChannel.AOBias = 0.7f; // Темнее

// G = Gloss
packingSettings.GreenChannel.SourcePath = "rock_gloss.png";
packingSettings.GreenChannel.ApplyToksvig = true;

// B = Metallic
packingSettings.BlueChannel.SourcePath = "rock_metallic.png";

// A = Height
packingSettings.AlphaChannel.SourcePath = "rock_height.png";

// UASTC для максимального качества
var compressionSettings = CompressionSettings.CreateUASTCDefault();
compressionSettings.UASTCQuality = 4;
compressionSettings.ColorSpace = ColorSpace.Linear;

var result = await pipeline.ConvertPackedTextureAsync(
    packingSettings,
    "rock_orm_highq.ktx2",
    compressionSettings
);
```

### Пример 3: Недостающие каналы с константами

```csharp
var packingSettings = ChannelPackingSettings.CreateDefault(ChannelPackingMode.OGM);

// R = AO (есть текстура)
packingSettings.RedChannel.SourcePath = "metal_ao.png";

// G = Gloss (нет текстуры, используем константу)
packingSettings.GreenChannel.SourcePath = null;
packingSettings.GreenChannel.DefaultValue = 0.8f; // Высокий глянец

// B = Metallic (есть текстура)
packingSettings.BlueChannel.SourcePath = "metal_metallic.png";
```

## Важные замечания

### Цветовое пространство

**КРИТИЧНО**: ORM текстуры ВСЕГДА используют Linear color space:

```csharp
compressionSettings.ColorSpace = ColorSpace.Linear;
```

Система автоматически переключит в Linear, но лучше устанавливать явно.

### Ручные мипмапы

Channel packing ТРЕБУЕТ ручной генерации мипмапов:

```csharp
compressionSettings.UseCustomMipmaps = true; // Автоматически установится
```

Это необходимо для применения Toksvig и AO processing к каждому уровню мипмапа.

### Форматы компрессии

Рекомендации:
- **ETC1S**: Для мобильных, малый размер (QualityLevel 128-192)
- **UASTC**: Для десктоп, высокое качество (UASTCQuality 2-4)

```csharp
// Для мобильных
var settings = CompressionSettings.CreateETC1SDefault();
settings.QualityLevel = 192; // Выше для ORM

// Для десктоп
var settings = CompressionSettings.CreateUASTCDefault();
settings.UASTCQuality = 4; // Максимальное качество
```

## Отладка

### Сохранение промежуточных мипмапов

```csharp
var result = await pipeline.ConvertPackedTextureAsync(
    packingSettings,
    "material_orm.ktx2",
    compressionSettings,
    saveSeparateMipmaps: true,
    mipmapOutputDir: "debug_mipmaps"
);
```

### Упаковка без компрессии (PNG)

```csharp
var channelPacker = new ChannelPackingPipeline();
var savedPaths = await channelPacker.PackAndSaveAsync(
    packingSettings,
    outputDirectory: "packed_png",
    baseName: "material_orm"
);
// Результат: material_orm_packed_mip0.png, _mip1.png, ...
```

## Batch обработка

```csharp
string[] materials = {
    "material01_albedo.png",
    "material02_albedo.png",
    "material03_albedo.png"
};

var detector = new ORMTextureDetector();
var pipeline = new TextureConversionPipeline();

foreach (var materialPath in materials) {
    var packingSettings = detector.CreateSettingsFromDetection(materialPath);
    if (packingSettings == null) continue;

    var baseName = Path.GetFileNameWithoutExtension(materialPath)
        .Replace("_albedo", "");

    var result = await pipeline.ConvertPackedTextureAsync(
        packingSettings,
        $"{baseName}_orm.ktx2",
        compressionSettings
    );
}
```

## Технические детали

### AOProcessor

Алгоритм BiasedDarkening:
```
target_value = lerp(mean, min, bias)
new_value = lerp(current_value, target_value, bias * 0.5)
```

Алгоритм Percentile:
```
percentile_value = histogram[percentile]
if value < percentile_value:
    new_value = lerp(value, percentile_value, 0.3)
```

### ChannelPackingPipeline

Workflow:
1. Загрузка исходных текстур для каждого канала
2. Генерация мипмапов с профилем для типа канала
3. Применение Toksvig для Gloss (если включено)
4. Применение AO processing для AO (если включено)
5. Упаковка по уровням мипмапов (mip0, mip1, ..., mipN)
6. Сохранение во временные PNG
7. Компрессия в KTX2 через ktx create

### Валидация

Автоматическая проверка:
- Соответствие режима и каналов (OG требует AO + Gloss)
- Toksvig только для Gloss канала
- AO processing только для AO канала
- Bias и Percentile в допустимых диапазонах

## Интеграция с PlayCanvas

PlayCanvas PBR материал использует ORM текстуру:
```javascript
material.aoMap = ormTexture; // R channel
material.glossMap = ormTexture; // G channel
material.metalnessMap = ormTexture; // B channel
material.useMetalness = true;

// GPU shader автоматически читает нужные каналы:
// vec3 ao = texture2D(aoMap, uv).rgb; // AO из RGB или R
// float gloss = texture2D(glossMap, uv).g; // Gloss из G или A
// float metalness = texture2D(metalnessMap, uv).b; // Metallic из B
```

## Дополнительные примеры

См. `TextureConversion/Examples/ChannelPackingExample.cs` для:
- Автоматическое обнаружение
- Ручная настройка OGMH
- OG режим для non-metallic
- Недостающие каналы с константами
- Упаковка без компрессии
- Batch обработка

## API Reference

### Классы

- `ChannelPackingMode` - Enum режимов упаковки
- `ChannelType` - Enum типов каналов
- `AOProcessingMode` - Enum режимов AO обработки
- `ChannelSourceSettings` - Настройки одного канала
- `ChannelPackingSettings` - Главный класс настроек
- `AOProcessor` - Процессор AO мипмапов
- `ORMTextureDetector` - Детектор текстур
- `ChannelPackingPipeline` - Пайплайн упаковки

### Методы

- `TextureConversionPipeline.ConvertPackedTextureAsync()` - Главный метод конвертации
- `ChannelPackingPipeline.PackChannelsAsync()` - Упаковка каналов
- `ChannelPackingPipeline.PackAndSaveAsync()` - Упаковка + сохранение PNG
- `ORMTextureDetector.DetectORMTextures()` - Поиск текстур
- `ORMTextureDetector.CreateSettingsFromDetection()` - Создание настроек

## Ограничения

- Все исходные текстуры должны быть одного размера (или будут ресайзнуты)
- Только монохромные (grayscale) текстуры для каждого канала
- Toksvig требует наличия normal map
- Компрессия Linear color space (не sRGB)
- Требуется ktx.exe версии 4.3.0 или выше

## Производительность

Типичное время для 2048x2048 OGM (4 канала):
- Генерация мипмапов: ~500ms
- Toksvig correction: ~300ms
- AO processing: ~200ms
- Упаковка: ~100ms
- KTX2 компрессия (ETC1S): ~2-5s
- **Итого**: ~3-6s

Для батча используйте параллельную обработку:
```csharp
var tasks = materials.Select(m => ProcessMaterialAsync(m));
await Task.WhenAll(tasks);
```
