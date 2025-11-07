# Histogram Metadata Specification
## Единый стандартизированный формат scale/offset

**Версия:** 2.0
**Дата:** 2025-11-07
**Статус:** ✅ УТВЕРЖДЁН И РЕАЛИЗОВАН

---

## Резюме

Система histogram preprocessing использует **единственный правильный формат** для хранения метаданных в KTX2 файлах:

- **ВСЕГДА** записываются **инвертированные** значения scale/offset
- **ВСЕГДА** используется квантование **Half16** (IEEE 754 half float)
- GPU применяет **прямое FMA** без дополнительных вычислений: `v = v_norm * scale + offset`

---

## Математическая основа

### Нормализация текстуры (CPU preprocessing)

**Цель:** Растянуть динамический диапазон текстуры на полный диапазон [0, 1]

```
1. Анализ гистограммы:
   lo = Percentile(0.5%)    // Нижняя граница (например: 0.0234)
   hi = Percentile(99.5%)   // Верхняя граница (например: 0.9843)

2. Параметры прямого преобразования:
   scale_forward = 1.0 / (hi - lo)
   offset_forward = -lo * scale_forward

3. Нормализация:
   v_normalized = v_original * scale_forward + offset_forward

   // Эквивалентно:
   v_normalized = (v_original - lo) / (hi - lo)
```

**Пример:**
- `lo = 0.0234`, `hi = 0.9843`
- `scale_forward = 1.0 / (0.9843 - 0.0234) = 1.0406`
- `offset_forward = -0.0234 * 1.0406 = -0.0243`

### Инверсия для GPU (для записи в файл)

**КРИТИЧЕСКИЙ МОМЕНТ:** Мы записываем в файл **обратное преобразование**, чтобы GPU мог применить простое FMA!

```
scale_inverse = 1.0 / scale_forward
offset_inverse = -offset_forward / scale_forward

// Эквивалентно:
scale_inverse = (hi - lo)
offset_inverse = lo
```

**Пример (продолжение):**
- `scale_inverse = 1.0 / 1.0406 = 0.9610`
- `offset_inverse = -(-0.0243) / 1.0406 = 0.0234`

### Денормализация на GPU (shader)

GPU читает **инвертированные** значения из файла и применяет простое FMA:

```glsl
// scale и offset прочитаны из KTX2 pc.meta (уже инвертированные!)
v_original = fma(v_normalized, scale, offset)

// Эквивалентно:
v_original = v_normalized * (hi - lo) + lo
```

**Пример (продолжение):**
- Нормализованное значение: `v_normalized = 0.5`
- `v_original = 0.5 * 0.9610 + 0.0234 = 0.5039` ✅

---

## Формат хранения в файле

### Правило определения формата

**НОВЫЙ ФОРМАТ (правильный):**
```
scale[0] < 1.0   →   GPU recovery values (hi - lo), готовы для использования
```

**СТАРЫЙ ФОРМАТ (устаревший, требует инверсии):**
```
scale[0] > 1.0   →   Normalization values 1/(hi - lo), нужна инверсия!
```

**Логика детекции:**
```csharp
bool needsInversion = metadata.Scale[0] > 1.0f;  // Если scale > 1.0 → старый формат
```

### Структура TLV блока

**Type-Length-Value формат:**

```
struct TLV {
    u8  type;       // 0x01 = HIST_SCALAR, 0x03 = HIST_PER_CHANNEL_3
    u8  flags;      // 0x10 = версия 1, квантование Half16
    u16 length;     // Длина payload (4 для scalar, 12 для per-channel RGB)
    u8  payload[];  // scale (half), offset (half), ...
    u8  padding[];  // Выравнивание до 4 байт
}
```

### Payload форматы

#### HIST_SCALAR (0x01) — Общий scale/offset

```
Offset | Size | Type | Value
-------|------|------|------------------
0      | 2    | half | scale_inverse
2      | 2    | half | offset_inverse
-------|------|------|------------------
TOTAL: 4 bytes
```

**Применение в shader:**
```glsl
color.rgb = fma(color.rgb, vec3(scale), vec3(offset));
```

#### HIST_PER_CHANNEL_3 (0x03) — Поканальный RGB

```
Offset | Size | Type  | Value
-------|------|-------|------------------
0      | 2    | half  | scale_inverse.r
2      | 2    | half  | scale_inverse.g
4      | 2    | half  | scale_inverse.b
6      | 2    | half  | offset_inverse.r
8      | 2    | half  | offset_inverse.g
10     | 2    | half  | offset_inverse.b
-------|------|-------|------------------
TOTAL: 12 bytes
```

**Применение в shader:**
```glsl
color.rgb = fma(color.rgb, scale_rgb, offset_rgb);
```

### Half Float квантование

**Формат:** IEEE 754 binary16
- **Диапазон:** ±65504
- **Точность:** ~3 десятичных знака (11 бит мантиссы)
- **Размер:** 2 байта

**Достаточно для histogram metadata:**
- Типичные значения scale: `[0.5, 2.0]`
- Типичные значения offset: `[-0.5, 0.5]`
- Ошибка квантования: `< 0.001` (незаметна при сжатии)

---

## Реализация в коде

### 1. Анализ и нормализация (HistogramAnalyzer.cs)

```csharp
public HistogramResult Analyze(Image<Rgba32> image, HistogramSettings settings) {
    // Строим гистограмму
    var histogram = BuildHistogram(image);

    // Вычисляем перцентили
    float lo = CalculatePercentile(histogram, settings.PercentileLow);
    float hi = CalculatePercentile(histogram, settings.PercentileHigh);

    // Параметры ПРЯМОГО преобразования (для нормализации)
    float scale = 1.0f / (hi - lo);
    float offset = -lo * scale;

    return new HistogramResult {
        Scale = new[] { scale },      // Прямое (для CPU)
        Offset = new[] { offset },    // Прямое (для CPU)
        RangeLow = lo,
        RangeHigh = hi
    };
}

public Image<Rgba32> ApplySoftKnee(Image<Rgba32> image, float lo, float hi, float knee) {
    // Применяет нормализацию: v_norm = (v - lo) / (hi - lo)
    // с soft-knee сглаживанием для выбросов
}
```

### 2. Инверсия для записи (TextureConversionPipeline.cs)

```csharp
// Создаём копию для TLV с инвертированными параметрами
var histogramForTLV = new HistogramResult {
    Scale = new float[histogramResult.Scale.Length],
    Offset = new float[histogramResult.Offset.Length],
    // ... остальные поля
};

// КРИТИЧНО: Инвертируем каждый канал
for (int i = 0; i < histogramResult.Scale.Length; i++) {
    float scale = histogramResult.Scale[i];
    float offset = histogramResult.Offset[i];

    histogramForTLV.Scale[i] = 1.0f / scale;          // scale_inv
    histogramForTLV.Offset[i] = -offset / scale;      // offset_inv
}

// Записываем инвертированные значения в TLV
tlvWriter.WriteHistogramResult(histogramForTLV, HistogramQuantization.Half16);
```

### 3. Запись в TLV (TLVWriter.cs)

```csharp
public void WriteHistogramResult(HistogramResult result, HistogramQuantization quantization) {
    TLVType tlvType;
    byte[] payload;

    if (result.ChannelMode == HistogramChannelMode.AverageLuminance) {
        tlvType = TLVType.HIST_SCALAR;
        payload = QuantizeScaleOffset(result.Scale[0], result.Offset[0], quantization);
    } else if (result.ChannelMode == HistogramChannelMode.PerChannel) {
        tlvType = TLVType.HIST_PER_CHANNEL_3;
        payload = QuantizeScaleOffsetArray(result.Scale, result.Offset, 3, quantization);
    }

    byte flags = 0x10 | ((byte)quantization << 2); // версия 1 + квантование
    WriteTLV(tlvType, flags, payload);
}

private byte[] QuantizeScaleOffset(float scale, float offset, HistogramQuantization quantization) {
    // ВСЕГДА Half16
    return HalfHelper.FloatsToHalfBytes(scale, offset);
}
```

### 4. Чтение и совместимость (Ktx2MetadataReader.cs)

```csharp
public static HistogramMetadata? ReadHistogramMetadata(string filePath) {
    // ... парсинг KTX2 заголовка и KVD ...

    var metadata = ParseKeyValueData(kvdData);

    // СОВМЕСТИМОСТЬ: Автодетект старого формата
    // NEW format: scale < 1.0 → GPU recovery values
    // OLD format: scale > 1.0 → normalization values (need inversion!)
    bool needsInversion = metadata.Scale[0] > 1.0f;

    if (needsInversion) {
        Logger.Warn("Detected OLD format (normalization values), inverting for GPU");

        // Инвертируем: scale_inv = 1/scale, offset_inv = -offset/scale
        for (int i = 0; i < metadata.Scale.Length; i++) {
            float s = metadata.Scale[i];
            float o = metadata.Offset[i];
            metadata.Scale[i] = 1.0f / s;
            metadata.Offset[i] = -o / s;
        }
    } else {
        Logger.Info("NEW format detected (scale < 1.0), using values directly");
    }

    return metadata;
}
```

### 5. Применение в шейдере (PlayCanvas/Unity)

**GLSL:**
```glsl
uniform float histScale;
uniform float histOffset;

void main() {
    vec4 color = texture(uTexture, vUV);

    // Прямое FMA (scale и offset уже инвертированные!)
    color.rgb = fma(color.rgb, vec3(histScale), vec3(histOffset));

    // Дальнейшая обработка...
}
```

**HLSL:**
```hlsl
float histScale;
float histOffset;

float4 main(PS_INPUT input) : SV_Target {
    float4 color = tex.Sample(samplerState, input.uv);

    // Прямое FMA
    color.rgb = color.rgb * histScale + histOffset;

    return color;
}
```

---

## Примеры работы

### Пример 1: Темная текстура (альбедо)

**Анализ:**
- Диапазон: `[0.02, 0.45]` (темная каменная текстура)
- `scale_forward = 1 / (0.45 - 0.02) = 2.326`
- `offset_forward = -0.02 * 2.326 = -0.0465`

**Нормализация:**
- Тёмный пиксель `0.10` → `0.10 * 2.326 - 0.0465 = 0.1861`
- Светлый пиксель `0.40` → `0.40 * 2.326 - 0.0465 = 0.8839`
- ✅ Диапазон растянут на [0, 1]

**Запись в файл (инвертированные):**
- `scale_inverse = 1 / 2.326 = 0.4299` ← **< 1.0** (новый формат)
- `offset_inverse = 0.0465 / 2.326 = 0.02`

**GPU восстановление:**
- Нормализованный `0.1861` → `0.1861 * 0.4299 + 0.02 = 0.10` ✅
- Нормализованный `0.8839` → `0.8839 * 0.4299 + 0.02 = 0.40` ✅

### Пример 2: HDR текстура (emissive)

**Анализ:**
- Диапазон: `[0.0, 5.8]` (HDR излучение)
- `scale_forward = 1 / 5.8 = 0.1724`
- `offset_forward = 0`

**Нормализация:**
- HDR значение `2.9` → `2.9 * 0.1724 = 0.5`
- HDR значение `5.8` → `5.8 * 0.1724 = 1.0`

**Запись в файл (инвертированные):**
- `scale_inverse = 1 / 0.1724 = 5.8`
- `offset_inverse = 0`

**GPU восстановление:**
- Нормализованный `0.5` → `0.5 * 5.8 + 0 = 2.9` ✅
- Нормализованный `1.0` → `1.0 * 5.8 + 0 = 5.8` ✅

---

## Верификация корректности

### Тест 1: Identity Transform

```csharp
// Без нормализации (Mode = Off)
scale_forward = 1.0
offset_forward = 0.0

// Инверсия
scale_inverse = 1 / 1.0 = 1.0
offset_inverse = -0 / 1.0 = 0.0

// GPU: v_out = v_in * 1.0 + 0.0 = v_in ✅
```

### Тест 2: Full Range

```csharp
// Полный диапазон [0, 1]
lo = 0.0, hi = 1.0
scale_forward = 1 / (1 - 0) = 1.0
offset_forward = -0 * 1.0 = 0.0

// Инверсия
scale_inverse = 1.0
offset_inverse = 0.0

// GPU: v_out = v_in ✅ (как и должно быть)
```

### Тест 3: Narrow Range

```csharp
// Узкий диапазон [0.4, 0.6]
lo = 0.4, hi = 0.6
scale_forward = 1 / 0.2 = 5.0
offset_forward = -0.4 * 5.0 = -2.0

// Нормализация
v_original = 0.5
v_normalized = 0.5 * 5.0 - 2.0 = 0.5

// Инверсия
scale_inverse = 1 / 5.0 = 0.2
offset_inverse = -(-2.0) / 5.0 = 0.4

// GPU восстановление
v_restored = 0.5 * 0.2 + 0.4 = 0.5 ✅
```

---

## Диагностика проблем

### Симптом: Текстура слишком темная или неправильные цвета

**Возможная причина:** Двойная инверсия - новый формат (scale < 1.0) был ошибочно инвертирован

**Решение:**
1. Проверить правило детекции: `needsInversion = metadata.Scale[0] > 1.0f` (НЕ `< 1.0f`!)
2. Новый формат (scale < 1.0) использовать напрямую
3. Старый формат (scale > 1.0) требует инверсии

**Правильный код проверки:**
```csharp
// ПРАВИЛЬНАЯ логика детекции
bool needsInversion = metadata.Scale[0] > 1.0f;

if (needsInversion) {
    Logger.Warn("OLD format detected, inverting...");
    for (int i = 0; i < metadata.Scale.Length; i++) {
        float s = metadata.Scale[i];
        float o = metadata.Offset[i];
        metadata.Scale[i] = 1.0f / s;
        metadata.Offset[i] = -o / s;
    }
} else {
    Logger.Info("NEW format, using directly");
}
```

### Симптом: Текстура слишком яркая

**Возможная причина:** Старый формат (scale > 1.0) не был инвертирован

**Решение:**
1. Убедиться что правило детекции: `needsInversion = metadata.Scale[0] > 1.0f`
2. Применить инверсию для старого формата

### Симптом: Текстура красная/розовая

**Возможная причина:** Shader применяет неправильное преобразование или metadata не читается

**Решение:**
1. Проверить что metadata загружается корректно (логи)
2. Убедиться что shader применяет: `v_original = v_normalized * scale + offset`
3. Проверить что histogram correction включена в UI

---

## Checklist для разработчиков

### При записи метаданных

- [ ] Анализ гистограммы вычисляет `scale_forward` и `offset_forward`
- [ ] Нормализация применяет прямое преобразование к текстуре
- [ ] Перед записью в TLV вычисляется инверсия: `scale_inv = 1/scale`, `offset_inv = -offset/scale`
- [ ] В файл записываются **только инвертированные** значения
- [ ] Используется квантование **Half16**
- [ ] TLV тип: `HIST_SCALAR (0x01)` или `HIST_PER_CHANNEL_3 (0x03)`
- [ ] Флаги: `0x10` (версия 1, Half16)

### При чтении метаданных

- [ ] Парсинг TLV извлекает scale и offset из payload
- [ ] Проверяется формат: `if (scale[0] < 1.0)` → новый формат
- [ ] Если старый формат → применить инверсию для совместимости
- [ ] Значения передаются в shader **без дополнительной обработки**

### В шейдере

- [ ] Используется простое FMA: `v = v_norm * scale + offset`
- [ ] **НЕТ** дополнительных вычислений или инверсий
- [ ] Uniform переменные: `histScale`, `histOffset`

---

## Заключение

### Единственный правильный формат:

1. **CPU:** Нормализует текстуру прямым преобразованием
2. **Файл:** Хранит **инвертированные** значения (scale < 1.0)
3. **GPU:** Применяет простое FMA для восстановления

### Преимущества:

- ✅ Простота на GPU (одна FMA операция)
- ✅ Совместимость с любыми шейдерами (GLSL, HLSL, Metal)
- ✅ Обратная совместимость через автодетект формата
- ✅ Компактность (4 байта Half16 для scalar)
- ✅ Математически точное восстановление

### Ссылки:

- Реализация анализа: `TextureConversion/Analysis/HistogramAnalyzer.cs`
- Реализация инверсии: `TextureConversion/Pipeline/TextureConversionPipeline.cs:316-349`
- Реализация записи: `TextureConversion/KVD/TLVWriter.cs`
- Реализация чтения: `TextureViewer/Ktx2MetadataReader.cs`
- Документация: `Docs/HistogramAnalysis.md`
