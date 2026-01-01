# meshopt_wrapper - Native Mesh Simplification DLL

Обёртка над [meshoptimizer](https://github.com/zeux/meshoptimizer) для симплификации мешей с сохранением UV атрибутов.

## Зачем это нужно?

gltfpack использует базовый `meshopt_simplify`, который может ломать UV seams.
Этот wrapper использует `meshopt_simplifyWithAttributes` с весами для UV координат,
что позволяет лучше сохранять текстурные швы при упрощении геометрии.

## Сборка

### Требования
- CMake 3.16+
- Visual Studio 2022 (Build Tools)
- Git

### Windows (PowerShell)

```powershell
cd Native/meshoptimizer
.\build.ps1
```

DLL будет скопирован в `bin/Release/.../win-x64/meshopt_wrapper.dll`

### Ручная сборка

```powershell
mkdir build
cd build
cmake .. -G "Visual Studio 17 2022" -A x64
cmake --build . --config Release
```

## API

### meshopt_simplify_with_uvs

Основная функция симплификации:

```c
void meshopt_simplify_with_uvs(
    unsigned int* destination,      // [out] Выходные индексы
    const unsigned int* indices,    // [in]  Входные индексы
    size_t index_count,             // Количество индексов
    const float* vertex_positions,  // [in]  Позиции (float3 * vertex_count)
    size_t vertex_count,            // Количество вершин
    size_t vertex_stride,           // sizeof(float) * 3
    const float* vertex_uvs,        // [in]  UV координаты (float2 * vertex_count)
    size_t uv_stride,               // sizeof(float) * 2
    const MeshoptSimplifyOptions* options,
    MeshoptSimplifyResult* result
);
```

### MeshoptSimplifyOptions

```c
typedef struct {
    size_t target_index_count;  // Целевое количество индексов (0 = использовать ratio)
    float target_ratio;         // Целевое соотношение (0.0-1.0)
    float target_error;         // Максимальная ошибка
    float uv_weight;            // Вес UV (1.0 = стандартный, 2.0+ = сильнее сохранять UV)
    int lock_border;            // Блокировать граничные вершины
    int error_is_absolute;      // Абсолютная ошибка
} MeshoptSimplifyOptions;
```

## Использование в C#

```csharp
using AssetProcessor.ModelConversion.Native;

// Проверка доступности
if (MeshOptimizer.IsAvailable()) {
    var options = MeshOptimizer.SimplifyOptions.FromRatio(
        ratio: 0.5f,      // 50% треугольников
        uvWeight: 1.5f    // Усиленное сохранение UV
    );

    uint[] newIndices = MeshOptimizer.SimplifyWithUvs(
        indices,
        positions,
        uvs,
        options
    );
}
```

## Рекомендации по UV weight

| Сценарий | UV Weight | Описание |
|----------|-----------|----------|
| LOD1 (близко) | 2.0 | Максимальное сохранение UV |
| LOD2 (средне) | 1.5 | Баланс качества и упрощения |
| LOD3 (далеко) | 1.0 | Стандартное упрощение |

## Лицензия

meshoptimizer: MIT License (c) Arseny Kapoulkine
wrapper: MIT License
