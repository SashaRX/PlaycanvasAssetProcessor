# Normal Maps - Руководство по настройке

## Два режима работы с ktx create

### 1️⃣ Single Image Mode (ktx create генерирует мипмапы)

**Когда использовать:**
- Когда нужно чтобы ktx create сам сгенерировал мипмапы из одного изображения
- Когда нужна быстрая конвертация без pre-processing

**Флаги ktx create:**
- `--generate-mipmap` - ktx create генерирует мипмапы
- `--normal-mode` - ✅ **МОЖНО** использовать (конвертирует XYZ → X+Y формат)
- `--normalize` - ✅ **МОЖНО** использовать (нормализует входные нормали)
- `--mipmap-filter <type>` - фильтр для генерации мипмапов

**Настройки в TexTool:**
```
GenerateMipmaps = true (но мипмапы генерирует ktx create, не MipGenerator)
ConvertToNormalMap = true  ✅ Работает
NormalizeVectors = true    ✅ Работает
ToktxMipFilter = Kaiser    ✅ Работает
```

**Недостаток:** ktx create НЕ умеет нормализовать нормали в процессе генерации мипмапов!

---

### 2️⃣ Pre-Generated Mipmaps Mode (MipGenerator + ktx create)

**Когда использовать:**
- Когда нужна правильная нормализация нормалей в мипмапах (критично для normal maps!)
- Когда нужен Toksvig anti-aliasing для gloss maps
- Когда нужны специальные фильтры или обработка

**Флаги ktx create:**
- Без `--generate-mipmap` - используются готовые мипмапы
- `--normal-mode` - ❌ **НЕ использовать** (конвертация должна быть сделана в MipGenerator)
- `--normalize` - ❌ **НЕ использовать** (несовместимо с pre-generated mipmaps)

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
3. **ktx create** получает готовые мипмапы и просто упаковывает их в KTX2
4. Флаги `--normal-mode` и `--normalize` не нужны и могут вызывать конфликты

---

## Проблема которую мы исправили

### ❌ Старая конфигурация (вызывала ошибку):
```
ConvertToNormalMap = true   → --normal-mode (конфликт!)
NormalizeVectors = true     → --normalize (конфликт!)
+ используются pre-generated mipmaps
```

**Результат:** ktx create exited with code 1 ❌

### ✅ Новая конфигурация (работает):
```
ConvertToNormalMap = false  → флаг не добавляется
NormalizeVectors = false    → флаг не добавляется
+ используются pre-generated mipmaps
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

**Документация ktx:**
https://github.khronos.org/KTX-Software/ktxtools/ktx.html

**Ключевые моменты из документации:**
- `--normal-mode` - "only valid for linear textures with two or more components"
- `--normalize` - "normalizes input normals to unit length"
- `--generate-mipmap` - автоматическая генерация мипмапов
- Для ASTC и ETC1S кодировщики имеют специальные настройки для normal maps

**Восстановление Z компонента в шейдере:**
```glsl
nml.z = sqrt(1.0 - dot(nml.xy, nml.xy));
```
