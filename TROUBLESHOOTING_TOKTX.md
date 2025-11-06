# Troubleshooting toktx Errors

## Проблема: toktx exited with code 1

Если вы получаете ошибку `toktx exited with code 1` при конвертации текстур, следуйте этим шагам:

---

## Шаг 1: Проверьте логи

1. Найдите файл `file.txt` в директории где запущен TexTool
2. Откройте его и найдите секцию `=== TOKTX COMMAND ===`
3. Скопируйте следующую информацию:
   - `toktx version:`
   - `Full command line:`
   - `Individual arguments:`

**Пример лога:**
```
=== TOKTX COMMAND ===
  Executable: toktx
  toktx version: v4.3.0
  Arguments count: 15
  Full command line:
  toktx --t2 --assign_oetf linear --encode uastc --uastc_quality 3 --uastc_rdo_l 1.0 --zcmp 3 --wmode clamp --mipmap --levels 5 output.ktx2 mip0.png mip1.png mip2.png mip3.png mip4.png
  Individual arguments:
    [0] = --t2
    [1] = --assign_oetf
    [2] = linear
    [3] = --encode
    [4] = uastc
    ...
```

---

## Шаг 2: Проверьте версию toktx

Откройте командную строку и выполните:
```bash
toktx --version
```

**Минимальная рекомендуемая версия:** v4.3.0 или выше

**Если версия ниже v4.3.0:**
- Флаг `--encode` может не поддерживаться (используйте `--bcmp` вместо этого)
- Флаг `--assign_oetf` может не поддерживаться
- Обновите KTX-Software до последней версии

**Установка/обновление toktx:**
- Windows: `winget install KhronosGroup.KTX-Software`
- Linux: Загрузите из https://github.com/KhronosGroup/KTX-Software/releases
- macOS: `brew install ktx`

---

## Шаг 3: Тестирование toktx вручную

Скопируйте командную строку из логов и попробуйте выполнить её вручную:

```bash
# Пример команды из логов (замените на вашу)
toktx --t2 --assign_oetf linear --encode uastc --uastc_quality 3 --uastc_rdo_l 1.0 --zcmp 3 --wmode clamp --mipmap --levels 5 C:/output.ktx2 C:/mip0.png C:/mip1.png C:/mip2.png C:/mip3.png C:/mip4.png
```

**Если команда не работает:**
1. Проверьте существуют ли входные файлы (mip0.png, mip1.png и т.д.)
2. Проверьте что пути не содержат недопустимых символов
3. Попробуйте упрощенную команду без некоторых флагов

---

## Шаг 4: Упрощенная команда для тестирования

Попробуйте минимальную команду:

```bash
# Минимальная команда с одним файлом
toktx --t2 --encode uastc output.ktx2 input.png

# Минимальная команда с мипмапами
toktx --t2 --encode uastc --mipmap --levels 3 output.ktx2 mip0.png mip1.png mip2.png
```

**Если минимальная команда работает** - проблема в дополнительных флагах
**Если минимальная команда НЕ работает** - проблема с установкой toktx или входными файлами

---

## Шаг 5: Известные проблемы и решения

### Проблема: "Unknown option --encode"
**Решение:** Обновите toktx до версии 4.3.0 или выше

### Проблема: "Unknown option --assign_oetf"
**Решение:** Обновите toktx до версии 4.1.0 или выше

### Проблема: "Unknown option --zcmp"
**Решение:** Обновите toktx до версии 4.0.0 или выше

### Проблема: Пути с пробелами
**Решение:** TexTool автоматически экранирует пути, но проверьте логи что пути в кавычках

### Проблема: Слишком много аргументов
**Решение:**
- Убедитесь что файлы мипмапов существуют
- Проверьте что `--levels` соответствует количеству файлов

---

## Шаг 6: Временное решение - упрощенный пресет

Если проблема не решается, создайте упрощенный пресет:

1. Откройте **Preset Management**
2. Создайте новый пресет с настройками:
   - `Name`: "Simple Normal"
   - `Compression Format`: UASTC
   - `Output Format`: KTX2
   - `UASTC Quality`: 2
   - `Use UASTC RDO`: false (отключить!)
   - `Generate Mipmaps`: true
   - `Color Space`: Auto (не Linear!)
   - `KTX2 Supercompression`: None (не Zstandard!)
   - Все остальное: по умолчанию

3. Попробуйте конвертировать с этим пресетом

---

## Шаг 7: Отправка отчета об ошибке

Если ничего не помогло, создайте issue на GitHub:
https://github.com/SashaRX/PlaycanvasAssetProcessor/issues

**Включите в отчет:**
1. Версию toktx (`toktx --version`)
2. Версию TexTool (из About window)
3. Полный лог из `file.txt` (секция TOKTX COMMAND)
4. Результат выполнения команды вручную
5. Операционную систему и версию

---

## Дополнительная информация

**Документация toktx:**
https://github.khronos.org/KTX-Software/ktxtools/toktx.html

**KTX-Software releases:**
https://github.com/KhronosGroup/KTX-Software/releases

**Настройка пресетов для normal maps:**
См. файл `TextureConversion/NORMAL_MAPS_GUIDE.md`
