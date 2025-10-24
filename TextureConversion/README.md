# Texture Conversion Pipeline for PlayCanvas

Система конвертации текстур с поддержкой Basis Universal для PlayCanvas проектов.

## Возможности

### Генерация мипмапов
- **Профили для разных типов текстур**: Albedo, Normal, Roughness, Metallic, AO, Emissive, Gloss
- **Множество фильтров**: Box, Bilinear, Bicubic, Lanczos3, Mitchell
- **Гамма-коррекция**: Автоматическая для sRGB текстур
- **Нормализация нормалей**: Для normal maps
- **Дополнительный blur**: Настраиваемый blur radius
- **Расширяемая архитектура**: Интерфейс IMipModifier для кастомных модификаторов

### Сжатие Basis Universal
- **Форматы**: ETC1S (меньший размер) и UASTC (высокое качество)
- **Выходные файлы**: .basis и .ktx2
- **Многопоточность**: Параллельная обработка
- **Предустановки**: Качество, размер, баланс
- **Сохранение мипмапов**: Возможность сохранения отдельных уровней для стриминга

### Пакетная обработка
- **Автоматическое определение типа**: По имени файла
- **Многопоточная обработка**: Настраиваемое количество потоков
- **Прогресс**: Отслеживание выполнения
- **Логирование**: Детальные логи через NLog

## Установка

### 1. Установка Basis Universal

Скачайте и установите basisu CLI encoder:

**Windows:**
```bash
# Через WinGet
winget install basisu

# Или скачайте вручную
# https://github.com/BinomialLLC/basis_universal/releases
# Распакуйте и добавьте в PATH
```

**Linux:**
```bash
# Сборка из исходников
git clone https://github.com/BinomialLLC/basis_universal.git
cd basis_universal
cmake -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build
sudo cp build/basisu /usr/local/bin/
```

**macOS:**
```bash
brew install basisu
```

### 2. Проверка установки

```bash
basisu -version
```

## Использование

### Базовая конвертация одного файла

```csharp
using AssetProcessor.TextureConversion.Core;
using AssetProcessor.TextureConversion.Pipeline;

// Создаем пайплайн
var pipeline = new TextureConversionPipeline();

// Создаем профиль для albedo текстуры
var mipProfile = MipGenerationProfile.CreateDefault(TextureType.Albedo);

// Настройки сжатия
var compressionSettings = CompressionSettings.CreateETC1SDefault();

// Конвертируем
var result = await pipeline.ConvertTextureAsync(
    inputPath: "textures/wood_albedo.png",
    outputPath: "output/wood_albedo.ktx2",
    mipProfile: mipProfile,
    compressionSettings: compressionSettings,
    saveSeparateMipmaps: false
);

if (result.Success) {
    Console.WriteLine($"Success! {result.MipLevels} mip levels generated in {result.Duration.TotalSeconds:F2}s");
} else {
    Console.WriteLine($"Failed: {result.Error}");
}
```

### Пакетная обработка директории

```csharp
using AssetProcessor.TextureConversion.Pipeline;

var pipeline = new TextureConversionPipeline();
var batchProcessor = new BatchProcessor(pipeline);

// Автоматический выбор профиля по имени файла
var profileSelector = BatchProcessor.CreateNameBasedProfileSelector();

var compressionSettings = CompressionSettings.CreateUASTCDefault();

// Прогресс
var progress = new Progress<BatchProgress>(p => {
    Console.WriteLine($"[{p.CurrentFile}/{p.TotalFiles}] {p.CurrentFileName} - {p.PercentComplete:F1}%");
});

// Обрабатываем все текстуры
var batchResult = await batchProcessor.ProcessDirectoryAsync(
    inputDirectory: "input_textures",
    outputDirectory: "output_compressed",
    profileSelector: profileSelector,
    compressionSettings: compressionSettings,
    saveSeparateMipmaps: true, // Сохранить отдельные мипмапы
    progress: progress,
    maxParallelism: 4
);

Console.WriteLine($"Completed: {batchResult.SuccessCount} succeeded, {batchResult.FailureCount} failed");
```

### Только генерация мипмапов (без сжатия)

```csharp
var pipeline = new TextureConversionPipeline();
var profile = MipGenerationProfile.CreateDefault(TextureType.Normal);

// Настраиваем профиль
profile.Filter = FilterType.Lanczos3;
profile.NormalizeNormals = true;
profile.MinMipSize = 4; // Не генерировать мипы меньше 4x4

var mipmapPaths = await pipeline.GenerateMipmapsOnlyAsync(
    inputPath: "normal_map.png",
    outputDirectory: "mipmaps",
    profile: profile
);

Console.WriteLine($"Generated {mipmapPaths.Count} mipmaps:");
foreach (var path in mipmapPaths) {
    Console.WriteLine($"  - {path}");
}
```

### Кастомный профиль генерации

```csharp
var customProfile = new MipGenerationProfile {
    TextureType = TextureType.Roughness,
    Filter = FilterType.Kaiser,
    ApplyGammaCorrection = false, // Roughness в линейном пространстве
    Gamma = 2.2f,
    BlurRadius = 0.5f, // Небольшой дополнительный blur
    IncludeLastLevel = true,
    MinMipSize = 1
};

// Можно добавить модификаторы
// customProfile.Modifiers.Add(new ToksvigModifier(normalMap, isGloss: false));
```

### Разные настройки сжатия

```csharp
// Максимальное качество (UASTC)
var highQuality = CompressionSettings.CreateHighQuality();

// Минимальный размер (ETC1S)
var minSize = CompressionSettings.CreateMinSize();

// Кастомные настройки
var custom = new CompressionSettings {
    CompressionFormat = CompressionFormat.UASTC,
    OutputFormat = OutputFormat.KTX2,
    UASTCQuality = 3,
    UseUASTCRDO = true,
    UASTCRDOQuality = 1.5f,
    GenerateMipmaps = false, // Мы уже сгенерировали
    UseMultithreading = true,
    ThreadCount = 8
};
```

## Профили мипмапов по типам текстур

### Albedo/Diffuse
- Фильтр: Kaiser (высокое качество)
- Гамма-коррекция: Да (sRGB → Linear → sRGB)
- Blur: Нет

### Normal Maps
- Фильтр: Kaiser
- Гамма-коррекция: Нет (уже в линейном пространстве)
- Нормализация: Да
- Blur: Нет
- Примечание: Готово для Toksvig модификатора

### Roughness
- Фильтр: Kaiser
- Гамма-коррекция: Нет
- Blur: Нет
- Примечание: Готово для Toksvig модификатора

### Metallic
- Фильтр: Box (часто бинарные значения)
- Гамма-коррекция: Нет
- Blur: Нет

### Ambient Occlusion
- Фильтр: Kaiser
- Гамма-коррекция: Нет
- Blur: Нет

### Emissive
- Фильтр: Kaiser
- Гамма-коррекция: Да (sRGB)
- Blur: Нет

## Архитектура

```
TextureConversion/
├── Core/                          # Базовые типы и интерфейсы
│   ├── TextureType.cs            # Типы текстур
│   ├── FilterType.cs             # Типы фильтров
│   ├── CompressionFormat.cs      # Форматы сжатия
│   ├── IMipModifier.cs           # Интерфейс модификаторов
│   ├── MipGenerationProfile.cs   # Профили генерации
│   └── CompressionSettings.cs    # Настройки сжатия
├── MipGeneration/                # Генерация мипмапов
│   ├── MipGenerator.cs           # Основной генератор
│   └── ToksvigModifier.cs        # Toksvig модификатор (TODO)
├── BasisU/                       # Интеграция Basis Universal
│   └── BasisUWrapper.cs          # CLI wrapper для basisu
└── Pipeline/                     # Пайплайн обработки
    ├── TextureConversionPipeline.cs  # Главный пайплайн
    └── BatchProcessor.cs             # Пакетная обработка
```

## Будущие улучшения

### Toksvig Gloss Modifier
Модификатор для коррекции roughness/gloss на основе дисперсии нормалей.

**Использование (когда будет реализовано):**
```csharp
// Загружаем normal map
using var normalMap = await Image.LoadAsync<Rgba32>("normal.png");

// Создаем модификатор
var toksvigModifier = new ToksvigModifier(normalMap, isGloss: false);

// Добавляем к профилю roughness
var roughnessProfile = MipGenerationProfile.CreateDefault(TextureType.Roughness);
roughnessProfile.Modifiers.Add(toksvigModifier);

// Теперь при генерации мипмапов roughness будет корректироваться
var result = await pipeline.ConvertTextureAsync(
    "roughness.png",
    "roughness.ktx2",
    roughnessProfile,
    compressionSettings
);
```

### Другие планируемые функции
- Автоматическая детекция типа текстуры по содержимому
- Поддержка HDR текстур (EXR, HDR)
- Генерация normal maps из height maps
- UI для настройки профилей
- Предпросмотр результатов сжатия
- Сравнение размеров и качества

## Советы по производительности

1. **Используйте UASTC для нормал мапов**: Лучшее качество для деталей
2. **ETC1S для albedo**: Хороший баланс размера/качества
3. **Многопоточность**: Установите `ThreadCount = 0` для автоопределения
4. **Отдельные мипмапы**: Сохраняйте для стриминга больших текстур
5. **Качество ETC1S**: 128 - хороший баланс, 192+ для высокого качества

## Примеры командной строки basisu

Система автоматически генерирует эти команды:

```bash
# ETC1S с мипмапами
basisu input.png -output_file output.ktx2 -ktx2 -q 128 -mipmap

# UASTC высокое качество
basisu input.png -output_file output.ktx2 -ktx2 -uastc -uastc_level 4

# С предгенерированными мипмапами
basisu input_mip0.png input_mip1.png input_mip2.png -output_file output.ktx2 -ktx2

# Многопоточное сжатие
basisu input.png -output_file output.ktx2 -ktx2 -max_threads 8
```

## Troubleshooting

### "basisu executable not found"
- Убедитесь что basisu установлен и доступен в PATH
- Или укажите полный путь: `new TextureConversionPipeline("C:/path/to/basisu.exe")`

### Медленная обработка
- Уменьшите `maxParallelism` в BatchProcessor
- Используйте ETC1S вместо UASTC
- Отключите RDO для UASTC

### Артефакты на normal maps
- Убедитесь что `NormalizeNormals = true`
- Попробуйте UASTC вместо ETC1S
- Увеличьте `UASTCQuality`

## Лицензия

MIT License - свободное использование в коммерческих и некоммерческих проектах.

## Ссылки

- [Basis Universal](https://github.com/BinomialLLC/basis_universal)
- [KTX2 Specification](https://www.khronos.org/ktx/)
- [PlayCanvas Engine](https://github.com/playcanvas/engine)
- [ImageSharp Documentation](https://docs.sixlabors.com/api/ImageSharp/)
