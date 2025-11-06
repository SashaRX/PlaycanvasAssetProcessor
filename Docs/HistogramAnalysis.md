# Histogram Analysis для оптимизации сжатия текстур

## Обзор

Система автоматического анализа гистограммы позволяет оптимизировать сжатие текстур путём вычисления параметров нормализации (`scale` и `offset`) на основе реального диапазона значений в текстуре. Это особенно полезно для текстур с узким динамическим диапазоном или наличием выбросов (outliers).

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

### HistogramMode

- **Off** — анализ отключён (scale=1, offset=0)
- **Percentile** — перцентили с жёстким клампированием
- **PercentileWithKnee** — перцентили + soft-knee (рекомендуется)
- **LocalOutlierPatch** — локальный анализ выбросов (зарезервировано)

### HistogramChannelMode

- **AverageLuminance** — усреднённая яркость `(R+G+B)/3`, один scale/offset
- **PerChannel** — поканальный анализ RGB (3 scale/offset)
- **RGBOnly** — анализ RGB, игнорируя альфа
- **PerChannelRGBA** — поканальный анализ RGBA (4 scale/offset)

## Настройки

### HistogramSettings

```csharp
public class HistogramSettings {
    public HistogramMode Mode { get; set; }
    public HistogramChannelMode ChannelMode { get; set; }

    // Перцентили (0.0-100.0)
    public float PercentileLow { get; set; } = 0.5f;
    public float PercentileHigh { get; set; } = 99.5f;

    // Ширина колена (0.0-1.0)
    public float KneeWidth { get; set; } = 0.02f;

    // Порог для предупреждений (0.0-1.0)
    public float TailThreshold { get; set; } = 0.005f;

    // Минимальный диапазон для нормализации
    public float MinRangeThreshold { get; set; } = 0.01f;
}
```

### Создание настроек

```csharp
// По умолчанию (отключено)
var settings = HistogramSettings.CreateDefault();

// С перцентилями
var settings = HistogramSettings.CreatePercentile(pLow: 0.5f, pHigh: 99.5f);

// С мягким коленом (рекомендуется)
var settings = HistogramSettings.CreateWithKnee(
    pLow: 0.5f,
    pHigh: 99.5f,
    knee: 0.02f
);
```

## Интеграция в CompressionSettings

```csharp
var compressionSettings = new CompressionSettings {
    CompressionFormat = CompressionFormat.ETC1S,
    QualityLevel = 128,

    // Включаем анализ гистограммы
    HistogramAnalysis = HistogramSettings.CreateWithKnee(),

    // Записывать параметры в KTX2
    WriteHistogramParams = true
};
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

## Рекомендуемые настройки по типам текстур

### HDR текстуры

```csharp
HistogramAnalysis = new HistogramSettings {
    Mode = HistogramMode.PercentileWithKnee,
    ChannelMode = HistogramChannelMode.AverageLuminance,
    PercentileLow = 0.1f,      // Консервативное отсечение
    PercentileHigh = 99.9f,
    KneeWidth = 0.03f,
    MinRangeThreshold = 0.05f
}
```

### Albedo/Diffuse

```csharp
HistogramAnalysis = new HistogramSettings {
    Mode = HistogramMode.PercentileWithKnee,
    ChannelMode = HistogramChannelMode.AverageLuminance,
    PercentileLow = 0.5f,
    PercentileHigh = 99.5f,
    KneeWidth = 0.02f
}
```

### Roughness/Metallic

```csharp
HistogramAnalysis = new HistogramSettings {
    Mode = HistogramMode.Percentile,  // Без колена для точности
    ChannelMode = HistogramChannelMode.AverageLuminance,
    PercentileLow = 0.5f,
    PercentileHigh = 99.5f,
    MinRangeThreshold = 0.005f
}
```

### Emissive

```csharp
HistogramAnalysis = new HistogramSettings {
    Mode = HistogramMode.PercentileWithKnee,
    ChannelMode = HistogramChannelMode.PerChannel,
    PercentileLow = 1.0f,
    PercentileHigh = 99.0f,
    KneeWidth = 0.05f
}
```

### Normal Maps

**Не рекомендуется** использовать анализ гистограммы для нормал мапов, так как они содержат направляющие векторы, а не яркостные значения.

```csharp
HistogramAnalysis = null  // Отключено
```

## Производительность

### Overhead

- **Анализ**: ~2-5 мс для текстуры 2048×2048 (параллельная обработка)
- **TLV метаданные**: 8-20 байт (пренебрежимо мало)
- **GPU применение**: 1 FMA операция на пиксель (очень быстро)

### Преимущества

- **Лучшее качество сжатия**: равномерное распределение гистограммы → меньше артефактов
- **Меньший размер**: оптимизированный диапазон → лучшая компрессия
- **Устойчивость к выбросам**: перцентили игнорируют единичные аномалии

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
