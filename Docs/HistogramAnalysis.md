# Histogram Analysis для оптимизации сжатия текстур

## Обзор

Система автоматического анализа гистограммы позволяет оптимизировать сжатие текстур путём вычисления параметров нормализации (`scale` и `offset`) на основе реального диапазона значений в текстуре. Это особенно полезно для текстур с узким динамическим диапазоном или наличием выбросов (outliers).

**ВАЖНО**: В текущей реализации histogram preprocessing — это **обязательная часть алгоритма**. Когда включен анализ гистограммы (`HistogramAnalysis != null`):
- Текстура **ВСЕГДА** нормализуется перед сжатием (preprocessing mode)
- Scale/offset **ВСЕГДА** записываются в KTX2 Key-Value Data для восстановления на GPU
- Scale/offset **ВСЕГДА** инвертируются для обратного преобразования
- Квантование **ВСЕГДА** используется Half16 (4 байта на канал)

## Принцип работы

### 1. Robust анализ с перцентилями

Вместо жёстких min/max значений используются **перцентили** для устойчивости к выбросам:

```
lo = Percentile(pLow)    // Например, 0.5%
hi = Percentile(pHigh)   // Например, 99.5%
```

Это позволяет игнорировать единичные аномальные пиксели, которые могут испортить нормализацию.

### 2. Soft-knee сглаживание

При использовании режима `PercentileWithKnee` применяется мягкое сглаживание для значений за пределами перцентилей:

```csharp
if (v < lo)  v' = lo - k * SmoothStep((lo - v) / k)
if (v > hi)  v' = hi + k * SmoothStep((v - hi) / k)
```

где `k = knee * (hi - lo)` — ширина колена, `SmoothStep(t) = 3t² - 2t³`.

### 3. Вычисление нормализации

```csharp
scale  = 1.0f / (hi - lo)
offset = -lo * scale
```

Эти параметры записываются в KTX2 Key-Value Data (KVD) и применяются на GPU:

```glsl
color = fma(color, scale, offset)
```

## Режимы анализа

### HistogramQuality (упрощённый API)

Начиная с последних версий, API упрощён до двух режимов качества:

- **HighQuality** (рекомендуется) — PercentileWithKnee (0.5%, 99.5%), knee=2%, soft-knee сглаживание
- **Fast** — Percentile (1%, 99%), жёсткое клампирование без soft-knee

Эти режимы автоматически устанавливают оптимальные параметры через factory методы `CreateHighQuality()` и `CreateFast()`.

### HistogramMode (внутренний, устанавливается автоматически)

- **Off** — анализ отключён (scale=1, offset=0)
- **Percentile** — перцентили с жёстким клампированием (используется в Fast mode)
- **PercentileWithKnee** — перцентили + soft-knee (используется в HighQuality mode)
- **LocalOutlierPatch** — локальный анализ выбросов (зарезервировано для будущего)

### HistogramChannelMode

- **AverageLuminance** — усреднённая яркость `(R+G+B)/3`, один scale/offset (рекомендуется)
- **PerChannel** — поканальный анализ RGB (3 scale/offset)
- **RGBOnly** — анализ RGB, игнорируя альфа
- **PerChannelRGBA** — поканальный анализ RGBA (4 scale/offset)

### HistogramQuantization (внутренний, всегда Half16)

- **Half16** — Half float (16-bit IEEE 754), всегда используется по умолчанию
- **PackedUInt32** — Packed uint32 (2×16-bit unsigned normalized), зарезервировано
- **Float32** — Float32 (32-bit IEEE 754), зарезервировано

## Настройки

### HistogramSettings (упрощённый API)

```csharp
public class HistogramSettings {
    // НОВЫЙ УПРОЩЁННЫЙ API
    public HistogramQuality Quality { get; set; } = HistogramQuality.HighQuality;

    // Внутренние параметры (устанавливаются автоматически через Quality)
    public HistogramMode Mode { get; set; } = HistogramMode.Off;
    public HistogramChannelMode ChannelMode { get; set; } = HistogramChannelMode.AverageLuminance;

    // Перцентили (0.0-100.0) - автоматически устанавливаются из Quality
    public float PercentileLow { get; set; } = 0.5f;
    public float PercentileHigh { get; set; } = 99.5f;

    // Ширина колена (0.0-1.0) - автоматически устанавливается из Quality
    public float KneeWidth { get; set; } = 0.02f;

    // Порог для предупреждений (0.0-1.0)
    public float TailThreshold { get; set; } = 0.005f;

    // Минимальный диапазон для нормализации
    public float MinRangeThreshold { get; set; } = 0.01f;
}
```

### Создание настроек (новый упрощённый API)

**Рекомендуемый способ (через factory методы):**

```csharp
// По умолчанию (отключено)
var settings = HistogramSettings.CreateDefault();
// Mode = Off

// High Quality (рекомендуется)
var settings = HistogramSettings.CreateHighQuality();
// Mode = PercentileWithKnee
// Quality = HighQuality
// PercentileLow = 0.5f, PercentileHigh = 99.5f, KneeWidth = 0.02f

// Fast Mode (грубая обработка)
var settings = HistogramSettings.CreateFast();
// Mode = Percentile
// Quality = Fast
// PercentileLow = 1.0f, PercentileHigh = 99.0f, KneeWidth = 0.0f
```

**Продвинутое использование (ручная настройка параметров):**

```csharp
// Пользовательские настройки на основе HighQuality
var settings = HistogramSettings.CreateHighQuality();
settings.ChannelMode = HistogramChannelMode.PerChannel;  // Поканальный анализ
settings.PercentileLow = 0.1f;   // Более консервативное отсечение для HDR
settings.PercentileHigh = 99.9f;
settings.KneeWidth = 0.03f;

// Применение пресета качества к существующим настройкам
var settings = new HistogramSettings();
settings.ApplyQualityPreset(HistogramQuality.HighQuality);
```

## Интеграция в CompressionSettings

**ВАЖНО**: При включении histogram analysis ОБЯЗАТЕЛЬНО устанавливайте `UseCustomMipmaps = true`!

```csharp
var compressionSettings = new CompressionSettings {
    CompressionFormat = CompressionFormat.ETC1S,
    QualityLevel = 128,
    GenerateMipmaps = true,
    UseCustomMipmaps = true,  // ОБЯЗАТЕЛЬНО для histogram preprocessing!

    // Включаем анализ гистограммы (High Quality)
    HistogramAnalysis = HistogramSettings.CreateHighQuality()
};
```

**Полный пример с ktx create:**

```csharp
var pipeline = new TextureConversionPipeline();

var settings = new CompressionSettings {
    CompressionFormat = CompressionFormat.ETC1S,
    QualityLevel = 128,
    GenerateMipmaps = true,
    UseCustomMipmaps = true,  // MipGenerator создаст мипмапы вручную
    ColorSpace = ColorSpace.SRGB,

    // Histogram preprocessing (ОБЯЗАТЕЛЬНАЯ часть алгоритма)
    HistogramAnalysis = HistogramSettings.CreateHighQuality()
    // Текстура будет нормализована, scale/offset запишутся в KTX2 KVD
};

var mipProfile = MipGenerationProfile.CreateDefault(TextureType.Albedo);

var result = await pipeline.ConvertTextureAsync(
    inputPath: "albedo.png",
    outputPath: "albedo.ktx2",
    mipProfile: mipProfile,
    compressionSettings: settings
);

if (result.Success && result.HistogramAnalysisResult != null) {
    Console.WriteLine($"Histogram preprocessing applied:");
    Console.WriteLine($"  Scale: {result.HistogramAnalysisResult.Scale[0]:F4}");
    Console.WriteLine($"  Offset: {result.HistogramAnalysisResult.Offset[0]:F4}");
}
```

## Формат хранения в KTX2

Метаданные записываются в KTX2 Key-Value Data в формате **TLV (Type-Length-Value)**:

### Структура TLV

```
struct TLV {
    u8  type;       // Тип блока
    u8  flags;      // Модификаторы/версия
    u16 length;     // Длина payload (LE)
    u8  payload[];  // Данные
    u8  padding[];  // Выравнивание до 4 байт
}
```

### Типы TLV блоков

#### 0x01 - HIST_SCALAR
Общий scale/offset для всех каналов:
- Payload: `half scale, half offset` (4 байта)
- Применение: `color = fma(color, scale, offset)`

#### 0x02 - HIST_RGB
Общий scale/offset для RGB (альфа не трогаем):
- Payload: `half scale, half offset` (4 байта)
- Применение: `color.rgb = fma(color.rgb, scale, offset)`

#### 0x03 - HIST_PER_CHANNEL_3
Поканально для RGB:
- Payload: `half3 scale, half3 offset` (12 байт)
- Применение: `color.rgb = fma(color.rgb, scale, offset)`

#### 0x04 - HIST_PER_CHANNEL_4
Поканально для RGBA:
- Payload: `half4 scale, half4 offset` (16 байт)
- Применение: `color = fma(color, scale, offset)`

#### 0x10 - HIST_PARAMS (опционально)
Параметры анализа:
- Payload: `half pLow, half pHigh, half knee` (6 байт)
- Flags: режим в битах [3:0]

### Ключ в KTX2 KVD

Все TLV блоки записываются под одним ключом: **`pc.meta`** (PlayCanvas metadata)

## Применение на GPU

### Чтение KVD в движке

```javascript
// WebGL/PlayCanvas
const kvData = ktx2File.keyValueData['pc.meta'];
const tlvBlocks = parseTLV(kvData);

// Извлекаем HIST блок (приоритет: HIST_PER_CHANNEL_4 > _3 > _RGB > _SCALAR)
const histBlock = tlvBlocks.find(b =>
    b.type >= 0x01 && b.type <= 0x04
);

if (histBlock) {
    const scale = readHalf(histBlock.payload, 0);
    const offset = readHalf(histBlock.payload, 2);

    // Передаём в material uniform
    material.setParameter('histScale', scale);
    material.setParameter('histOffset', offset);
}
```

### Применение в шейдере

```glsl
// Vertex или Fragment shader
uniform float histScale;
uniform float histOffset;

void main() {
    vec4 color = texture(uTexture, vUV);

    // Применяем FMA для денормализации
    color.rgb = fma(color.rgb, vec3(histScale), vec3(histOffset));

    // Дальнейшая обработка...
}
```

### Поканальный режим

```glsl
uniform vec3 histScale;    // Отдельные scale для RGB
uniform vec3 histOffset;   // Отдельные offset для RGB

void main() {
    vec4 color = texture(uTexture, vUV);
    color.rgb = fma(color.rgb, histScale, histOffset);
}
```

## Рекомендуемые настройки по типам текстур (новый API)

### HDR текстуры

```csharp
// Используем HighQuality с консервативными перцентилями
var settings = HistogramSettings.CreateHighQuality();
settings.PercentileLow = 0.1f;       // Более консервативное отсечение
settings.PercentileHigh = 99.9f;
settings.KneeWidth = 0.03f;          // Более широкое колено
settings.MinRangeThreshold = 0.05f;  // Больший порог для HDR

HistogramAnalysis = settings;
```

### Albedo/Diffuse

```csharp
// Используем настройки по умолчанию (HighQuality)
HistogramAnalysis = HistogramSettings.CreateHighQuality();
// Эквивалентно:
// Mode = PercentileWithKnee
// PercentileLow = 0.5f, PercentileHigh = 99.5f
// KneeWidth = 0.02f
// ChannelMode = AverageLuminance
```

### Roughness/Metallic

```csharp
// Используем Fast mode (без soft-knee для точности)
HistogramAnalysis = HistogramSettings.CreateFast();
// Эквивалентно:
// Mode = Percentile (жёсткое клампирование)
// PercentileLow = 1.0f, PercentileHigh = 99.0f
// KneeWidth = 0.0f (без колена)

// Опционально: уменьшаем порог для узких диапазонов
var settings = HistogramSettings.CreateFast();
settings.MinRangeThreshold = 0.005f;
HistogramAnalysis = settings;
```

### Emissive

```csharp
// Fast mode с поканальным анализом
var settings = HistogramSettings.CreateFast();
settings.ChannelMode = HistogramChannelMode.PerChannel;  // Для цветных источников света
settings.KneeWidth = 0.05f;  // Добавляем широкое колено для ярких пикселей
HistogramAnalysis = settings;
```

### Normal Maps

**Не рекомендуется** использовать анализ гистограммы для нормал мапов, так как они содержат направляющие векторы, а не яркостные значения.

```csharp
HistogramAnalysis = null  // Отключено
// ИЛИ
HistogramAnalysis = HistogramSettings.CreateDefault()  // Mode = Off
```

Для normal maps используйте `UseCustomMipmaps = false` и включите `ConvertToNormalMap = true` и `NormalizeVectors = true` в `CompressionSettings`, чтобы ktx create правильно обработал их.

## Интеграция с ktx create и metadata injection

### Workflow с ktx.exe (новый подход)

Начиная с ktx 4.3.0, используется команда `ktx create` вместо устаревшего `toktx.exe`:

**Шаг 1: Manual Mipmap Generation**
```csharp
// MipGenerator создаёт все мипмапы вручную с нужным фильтром
UseCustomMipmaps = true
var mipmaps = _mipGenerator.GenerateMipmaps(sourceImage, mipProfile);
// Результат: mip0 (2048x2048), mip1 (1024x1024), ..., mip10 (1x1)
```

**Шаг 2: Histogram Preprocessing**
```csharp
// Анализируем mip0 для вычисления scale/offset
var histogramResult = _histogramAnalyzer.Analyze(mip0, histogramSettings);

// Применяем нормализацию ко ВСЕМ мипмапам
for each mipmap:
    ApplySoftKnee() or ApplyWinsorization()

// Инвертируем scale/offset для GPU recovery
scale_inv = 1.0 / scale
offset_inv = -offset / scale
```

**Шаг 3: TLV Metadata Creation**
```csharp
using var tlvWriter = new TLVWriter();

// Записываем результат с инвертированными параметрами
tlvWriter.WriteHistogramResult(histogramForTLV, HistogramQuantization.Half16);

// Записываем параметры анализа (для отладки)
tlvWriter.WriteHistogramParams(histogramSettings);

// Сохраняем в временный файл
tlvWriter.SaveToFile("pc.meta.bin");
```

**Шаг 4: ktx create Packing**
```bash
ktx create --format R8G8B8A8_SRGB --encode basis-lz \
  --clevel 1 --qlevel 128 \
  mip0.png mip1.png mip2.png ... output.ktx2
```

**ВАЖНО**: ktx create **НЕ ПОДДЕРЖИВАЕТ** добавление KVD через командную строку!

**Шаг 5: Post-Processing Metadata Injection**
```csharp
// Используем Ktx2BinaryPatcher для прямого редактирования KTX2 файла
var patcher = new Ktx2BinaryPatcher();

// Загружаем KTX2 файл, добавляем KVD секцию, обновляем Level Index Array
patcher.InjectMetadata(outputPath, kvdBinaryFiles);

// Результат: KTX2 файл с встроенными метаданными
```

### Ktx2BinaryPatcher Details

Класс `Ktx2BinaryPatcher` напрямую манипулирует бинарным форматом KTX2:

1. **Чтение заголовка**: Парсит KTX2 Index (дескриптор уровней и метаданных)
2. **Резервирование места**: Вычисляет новый размер kvdByteLength
3. **Сдвиг Level Index Array**: Обновляет byteOffset для каждого уровня мипмапа
4. **Запись KVD**: Вставляет Key-Value Data между заголовком и уровнями
5. **Обновление заголовка**: Записывает новые kvdByteOffset и kvdByteLength

**Формат KVD записи**:
```
keyLength (uint32)           // Длина ключа (7 для "pc.meta")
key (UTF-8 string + padding) // "pc.meta\0" + padding до 4 байт
valueLength (uint32)         // Длина TLV данных
value (binary)               // TLV блоки (HIST_SCALAR + HIST_PARAMS)
padding                      // Выравнивание до 4 байт
```

### Zstd Supercompression Limitation

**КРИТИЧЕСКАЯ ИНФОРМАЦИЯ**: Zstandard supercompression работает ТОЛЬКО с UASTC!

```csharp
// ✓ ПРАВИЛЬНО: UASTC + Zstd
CompressionFormat = CompressionFormat.UASTC,
KTX2Supercompression = KTX2SupercompressionType.Zstandard,
KTX2ZstdLevel = 3

// ✗ НЕПРАВИЛЬНО: ETC1S + Zstd (игнорируется ktx create)
CompressionFormat = CompressionFormat.ETC1S,
KTX2Supercompression = KTX2SupercompressionType.Zstandard  // НЕ ПОДДЕРЖИВАЕТСЯ!
```

Для ETC1S используйте `KTX2SupercompressionType.None` или не указывайте суперкомпрессию.

## Производительность

### Overhead

- **Анализ**: ~2-5 мс для текстуры 2048×2048 (параллельная обработка)
- **Preprocessing**: ~10-20 мс для нормализации всех мипмапов
- **TLV метаданные**: 8-20 байт (пренебрежимо мало)
- **Metadata injection**: ~1-2 мс (бинарное редактирование файла)
- **GPU применение**: 1 FMA операция на пиксель (очень быстро)

### Преимущества

- **Лучшее качество сжатия**: равномерное распределение гистограммы → меньше артефактов
- **Меньший размер**: оптимизированный диапазон → лучшая компрессия
- **Устойчивость к выбросам**: перцентили игнорируют единичные аномалии
- **Обратимость**: GPU shader полностью восстанавливает оригинальный диапазон

## Диагностика

### Warnings

Система генерирует предупреждения при:

- **Высокая доля хвостов** (`> TailThreshold`): много пикселей за пределами перцентилей
  - Возможно, шум или артефакты в текстуре
  - Рассмотрите более консервативные перцентили

- **Слишком узкий диапазон** (`< MinRangeThreshold`): почти константная текстура
  - Нормализация пропущена (усиление шума)

### Логирование

Включите NLog уровень `Info` для просмотра деталей анализа:

```
=== HISTOGRAM ANALYSIS START ===
  Mode: PercentileWithKnee
  Channel Mode: AverageLuminance
  Percentiles: 0.5% - 99.5%
  Image size: 2048x2048
  Percentiles: lo=0.0234 (6/255), hi=0.9843 (251/255)
  Tail fraction: 0.42%
  Soft-knee applied: knee width = 0.0192
=== HISTOGRAM ANALYSIS COMPLETE ===
  Scale: [1.0406]
  Offset: [-0.0243]
  Range: [0.0234 - 0.9843]
  Knee applied: True
```

## Примеры использования

См. `TextureConversion/Examples/HistogramAnalysisExample.cs` для полных примеров кода.

## Ссылки

- [TLV формат спецификация](/Docs/TLV-Format-Spec.md)
- [KTX2 спецификация](https://registry.khronos.org/KTX/specs/2.0/ktxspec.v2.html)
- [Basis Universal](https://github.com/BinomialLLC/basis_universal)
