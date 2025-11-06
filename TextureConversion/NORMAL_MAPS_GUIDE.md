# Normal Maps - Руководство по настройке

## Два режима работы с toktx

### 1️⃣ Single Image Mode (toktx генерирует мипмапы)

**Когда использовать:**
- Когда нужно чтобы toktx сам сгенерировал мипмапы из одного изображения
- Когда нужна быстрая конвертация без pre-processing

**Флаги toktx:**
- `--genmipmap` - toktx генерирует мипмапы
- `--normal_mode` - ✅ **МОЖНО** использовать (конвертирует XYZ → X+Y формат)
- `--normalize` - ✅ **МОЖНО** использовать (нормализует входные нормали)
- `--filter <type>` - фильтр для генерации мипмапов

**Настройки в TexTool:**
```
GenerateMipmaps = true (но мипмапы генерирует toktx, не MipGenerator)
ConvertToNormalMap = true  ✅ Работает
NormalizeVectors = true    ✅ Работает
ToktxMipFilter = Kaiser    ✅ Работает
```

**Недостаток:** toktx НЕ умеет нормализовать нормали в процессе генерации мипмапов!

---

### 2️⃣ Pre-Generated Mipmaps Mode (MipGenerator + toktx)

**Когда использовать:**
- Когда нужна правильная нормализация нормалей в мипмапах (критично для normal maps!)
- Когда нужен Toksvig anti-aliasing для gloss maps
- Когда нужны специальные фильтры или обработка

**Флаги toktx:**
- `--mipmap` - используются готовые мипмапы
- `--levels N` - количество уровней мипмапов
- `--normal_mode` - ❌ **НЕ использовать** (конвертация должна быть сделана в MipGenerator)
- `--normalize` - ❌ **НЕ использовать** (несовместимо с --mipmap)

**Настройки в TexTool:**
```
GenerateMipmaps = true (мипмапы генерирует MipGenerator)
NormalizeNormals = true    ✅ Нормализация в MipGenerator
ConvertToNormalMap = false ❌ НЕ использовать с pre-generated mipmaps
NormalizeVectors = false   ❌ НЕ использовать с pre-generated mipmaps
MipFilter = Kaiser         ✅ Используется в MipGenerator
```

**Преимущество:** Правильная нормализация нормалей в каждом мипмапе!

---

## Встроенный пресет "Normal (Linear)"

Текущая конфигурация (исправленная):

```csharp
CompressionFormat = UASTC          // Высокое качество для нормалей
ColorSpace = Linear                // Правильное для normal maps
NormalizeNormals = true            ✅ Нормализация в MipGenerator
ConvertToNormalMap = false         ❌ ОТКЛЮЧЕНО (pre-generated mipmaps)
NormalizeVectors = false           ❌ ОТКЛЮЧЕНО (pre-generated mipmaps)
MipFilter = Kaiser                 ✅ Высококачественный фильтр
ApplyGammaCorrection = false       ✅ Правильно для linear данных
```

**Почему это правильно:**
1. **MipGenerator** генерирует мипмапы с нормализацией нормалей (`NormalizeNormals = true`)
2. Каждый мипмап имеет **правильные единичные нормали**
3. **toktx** получает готовые мипмапы и просто упаковывает их в KTX2
4. Флаги `--normal_mode` и `--normalize` не нужны и могут вызывать конфликты

---

## Проблема которую мы исправили

### ❌ Старая конфигурация (вызывала ошибку):
```
ConvertToNormalMap = true   → --normal_mode (конфликт!)
NormalizeVectors = true     → --normalize (конфликт!)
+ используются pre-generated mipmaps (--mipmap --levels N)
```

**Результат:** toktx exited with code 1 ❌

### ✅ Новая конфигурация (работает):
```
ConvertToNormalMap = false  → флаг не добавляется
NormalizeVectors = false    → флаг не добавляется
+ используются pre-generated mipmaps (--mipmap --levels N)
```

**Результат:** Успешная упаковка ✅

---

## Рекомендации

### Для Normal Maps:
- ✅ Используйте **Pre-Generated Mipmaps Mode** (текущий режим TexTool)
- ✅ Включите `NormalizeNormals = true` в пресете
- ✅ Используйте `ColorSpace = Linear`
- ✅ Используйте `UASTC` для максимального качества
- ✅ Используйте фильтр `Kaiser` или `Lanczos3`
- ❌ Не используйте `ConvertToNormalMap` с pre-generated mipmaps
- ❌ Не используйте `NormalizeVectors` с pre-generated mipmaps

### Для Gloss Maps с Toksvig:
- ✅ Используйте **Pre-Generated Mipmaps Mode** (обязательно!)
- ✅ Включите `ToksvigSettings.Enabled = true`
- ✅ Укажите путь к normal map в `ToksvigSettings.NormalMapPath`
- ✅ Используйте `ColorSpace = Linear`

---

## Дополнительная информация

**Документация toktx:**
https://github.khronos.org/KTX-Software/ktxtools/toktx.html

**Ключевые моменты из документации:**
- `--normal_mode` - "only valid for linear textures with two or more components"
- `--normalize` - "normalizes input normals to unit length"
- `--mipmap` - "mutually exclusive with --automipmap and --genmipmap"
- Для ASTC и ETC1S кодировщики имеют специальные настройки для normal maps

**Восстановление Z компонента в шейдере:**
```glsl
nml.z = sqrt(1.0 - dot(nml.xy, nml.xy));
```
