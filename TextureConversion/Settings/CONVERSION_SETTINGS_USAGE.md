# Conversion Settings - Использование

## Архитектура

Система настроек конвертации текстур состоит из следующих компонентов:

### 1. ConversionParameter.cs
Базовые классы для описания параметров:
- `ParameterUIType` - тип UI элемента (checkbox, dropdown, slider, etc.)
- `ParameterSection` - секция параметров (Preset, Compression, Alpha, etc.)
- `VisibilityCondition` - условия видимости параметра
- `ConversionParameter` - полное описание одного параметра
- `ParameterGroup` - группа параметров (секция)
- `ConversionPreset` - пресет настроек

### 2. ConversionSettingsSchema.cs
Полная схема всех доступных параметров:
- `GetAllParameterGroups()` - все группы параметров
- `GetPredefinedPresets()` - предопределенные пресеты

### 3. ConversionSettingsManager.cs
Менеджер для управления текущими настройками:
- `SetValue()` / `GetValue()` - установка/получение значений
- `ApplyPreset()` - применение пресета
- `GenerateToktxArguments()` - генерация CLI аргументов для toktx
- `GetInternalSettings()` - получение внутренних параметров для препроцессинга

## Секции параметров

### 1. Preset
Выбор базового профиля, который меняет дефолты других секций:
- **Albedo/Color (sRGB)** - для цветных текстур в sRGB
- **Normal (Linear)** - для карт нормалей
- **AO/Gloss/Roughness (Linear + Toksvig)** - для gloss/roughness с коррекцией
- **Height (Linear with Clamp)** - для карт высот

### 2. Compression Settings
Управление типом кодека и параметрами качества:
- `compressionFormat` (dropdown) - ETC1S / UASTC / ASTC
- `compressionLevel` (slider, 0-5) - только для ETC1S
- `qualityLevel` (slider, 1-255) - для ETC1S
- `uastcQuality` (slider, 0-4) - для UASTC
- `astcQuality` (dropdown) - для ASTC
- `perceptualMode` (checkbox) - визуальное улучшение
- `useRDO` (checkbox) - Rate-Distortion Optimization
- `rdoLambda` (numeric, 0.01-10.0) - параметр RDO для UASTC
- `supercompression` (slider, 1-22) - Zstd суперкомпрессия
- `threads` (dropdown) - Auto / 1 / 2 / 4 / 8 / 16 / 32

### 3. Alpha Options
Управление альфа-каналом:
- `forceAlpha` (checkbox) - принудительно добавить альфа
- `removeAlpha` (checkbox) - удалить альфа
- `separateRGAlpha` (checkbox) - разделить RG для XY нормалей

### 4. Color Space
Управление цветовым пространством:
- `colorSpace` (radio) - auto / linear / srgb
- `treatAsLinear` (checkbox) - принудительно linear
- `treatAsSRGB` (checkbox) - принудительно sRGB

### 5. Mipmaps
Настройки генерации мипмапов:
- `generateMipmaps` (checkbox) - всегда true (внутренний)
- `mipFilter` (dropdown) - kaiser, lanczos3, mitchell, etc.
- `linearMipFiltering` (checkbox) - линейная фильтрация
- `clampMipEdges` (checkbox) - ограничить края
- `removeTemporalMipmaps` (checkbox) - очистить временные файлы
- `normalizeMipmaps` (checkbox) - нормализовать векторы

### 6. Normal Maps
Специальные настройки для карт нормалей:
- `convertToXYNormal` (checkbox) - конвертировать RGB→XY
- `normalizeVectors` (checkbox) - нормализовать векторы
- `keepRGBLayout` (checkbox) - оставить RGB структуру

### 7. Toksvig (Anti-Aliasing)
Коррекция Toksvig для gloss/roughness:
- `enableToksvig` (checkbox) - включить коррекцию
- `smoothVariance` (checkbox) - сглаживание вариации
- `compositePower` (numeric, 0.1-5.0) - степень смешивания
- `toksvigMinMipLevel` (numeric, 0-12) - базовый уровень
- `toksvigNormalMapPath` (file) - путь к normal map

### 8. Actions
Кнопки действий:
- `convertSelected` (button) - конвертировать выбранные
- `applyPreset` (button) - применить и сохранить пресет
- `reset` (button) - сбросить к дефолтам

## Пример использования

```csharp
// 1. Создаем менеджер настроек
var globalSettings = TextureConversionSettingsManager.LoadSettings();
var settingsManager = new ConversionSettingsManager(globalSettings);

// 2. Применяем пресет
settingsManager.ApplyPresetByName("Normal (Linear)");

// 3. Настраиваем параметры
settingsManager.SetValue("compressionFormat", "uastc");
settingsManager.SetValue("uastcQuality", 3);
settingsManager.SetValue("normalizeVectors", true);

// 4. Получаем внутренние настройки для препроцессинга
var internalSettings = settingsManager.GetInternalSettings();

// 5. Генерируем мипмапы с учетом настроек
var mipGenerator = new MipGenerator();
var mipProfile = MipGenerationProfile.CreateDefault(TextureType.Normal);
mipProfile.Filter = ParseFilterType(internalSettings.MipFilter);
var mipmaps = mipGenerator.GenerateMipmaps(sourceImage, mipProfile);

// 6. Применяем Toksvig если включено
if (internalSettings.EnableToksvig) {
    var toksvigSettings = new ToksvigSettings {
        Enabled = true,
        CompositePower = internalSettings.CompositePower,
        NormalMapPath = internalSettings.ToksvigNormalMapPath
    };
    // ... apply Toksvig
}

// 7. Сохраняем мипмапы во временные файлы
var tempMipmapPaths = new List<string>();
for (int i = 0; i < mipmaps.Count; i++) {
    var mipPath = Path.Combine(tempDir, $"mip{i}.png");
    await mipmaps[i].SaveAsPngAsync(mipPath);
    tempMipmapPaths.Add(mipPath);
}

// 8. Генерируем CLI аргументы для toktx
var outputPath = "output.ktx2";
var args = settingsManager.GenerateToktxArguments(outputPath, tempMipmapPaths);

// 9. Запускаем toktx
var toktxWrapper = new ToktxWrapper("toktx");
var result = await toktxWrapper.PackMipmapsAsync(tempMipmapPaths, outputPath, compressionSettings);

// 10. Очищаем временные файлы
if (internalSettings.RemoveTemporalMipmaps) {
    Directory.Delete(tempDir, recursive: true);
}
```

## CLI Mapping

### Compression
- `--encode etc1s` - ETC1S формат
- `--encode uastc` - UASTC формат
- `--encode astc` - ASTC формат
- `--clevel 0-5` - уровень компрессии ETC1S
- `--qlevel 1-255` - качество ETC1S
- `--uastc_quality 0-4` - качество UASTC
- `--astc_quality <fastest|fast|medium|thorough|exhaustive>` - качество ASTC
- `--uastc_rdo_l <λ>` - RDO lambda для UASTC
- `--zcmp 1-22` - Zstandard суперкомпрессия

### Alpha
- `--target_type RGBA` - принудительно RGBA
- `--target_type RGB` - принудительно RGB

### Color Space
- `--assign_oetf linear` - linear пространство
- `--assign_oetf srgb` - sRGB пространство

### Normal Maps
- `--normal_mode` - режим normal map (XY layout)
- `--normalize` - нормализовать векторы
- `--input_swizzle rgb1` - оставить RGB структуру

### Mipmaps
- `--genmipmap` - генерировать мипмапы (НЕ используется, т.к. мы генерируем сами)
- `--filter <name>` - фильтр (НЕ используется, т.к. используется внутренний MipGenerator)
- `--wmode clamp` - ограничить края

### Performance
- `--threads <count>` - количество потоков

## Внутренние параметры (не генерируют CLI)

Эти параметры используются для препроцессинга перед вызовом toktx:

1. **Mipmaps**
   - `generateMipmaps` - всегда true
   - `mipFilter` - используется MipGenerator
   - `linearMipFiltering` - используется MipGenerator
   - `removeTemporalMipmaps` - очистка временных файлов

2. **Toksvig**
   - `enableToksvig` - включить коррекцию
   - `smoothVariance` - сглаживание
   - `compositePower` - степень смешивания
   - `toksvigMinMipLevel` - базовый уровень
   - `toksvigNormalMapPath` - путь к normal map

3. **Alpha**
   - `separateRGAlpha` - разделение RG каналов

4. **Other**
   - `colorSpace` - авто определение цветового пространства
   - `perceptualMode` - perceptual режим (авто для ETC1S)

## Условная видимость

Некоторые параметры видны только при определенных условиях:

```csharp
// Compression Level видим только для ETC1S
Visibility = new VisibilityCondition {
    DependsOnParameter = "compressionFormat",
    RequiredValue = "etc1s"
}

// Toksvig настройки видны только если enableToksvig = true
Visibility = new VisibilityCondition {
    DependsOnParameter = "enableToksvig",
    RequiredValue = true
}
```

## Валидация

Менеджер автоматически валидирует значения:

```csharp
// Slider/Numeric автоматически ограничиваются MinValue/MaxValue
settingsManager.SetValue("qualityLevel", 300); // Будет 255 (MaxValue)
settingsManager.SetValue("rdoLambda", -1.0);   // Будет 0.01 (MinValue)
```

## Threads - специальная обработка

Параметр `threads` имеет специальную логику:

- **"Auto"** - использует `globalSettings.ThreadCount`
  - Если `ThreadCount == 0`, флаг `--threads` НЕ добавляется (toktx использует автоопределение)
  - Если `ThreadCount > 0`, добавляется `--threads <count>`
- **"1", "2", "4", etc.** - явное значение, добавляется `--threads <value>`

```csharp
// В GenerateToktxArguments:
if (threadsValue == "Auto") {
    if (_globalSettings.ThreadCount > 0) {
        args.Add("--threads");
        args.Add(_globalSettings.ThreadCount.ToString());
    }
    // Если 0, не добавляем флаг
} else {
    args.Add("--threads");
    args.Add(threadsValue);
}
```

## Экспорт/Импорт настроек

```csharp
// Экспорт в словарь
var settings = settingsManager.ExportSettings();

// Сохранение в JSON
var json = JsonSerializer.Serialize(settings);
File.WriteAllText("preset.json", json);

// Загрузка из JSON
var json = File.ReadAllText("preset.json");
var settings = JsonSerializer.Deserialize<Dictionary<string, object?>>(json);
settingsManager.ImportSettings(settings);
```
