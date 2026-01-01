/**
 * meshopt_wrapper.h - C wrapper for meshoptimizer simplification with UV attributes
 *
 * Provides P/Invoke compatible exports for mesh simplification that preserves UV seams
 * using meshopt_simplifyWithAttributes instead of basic meshopt_simplify.
 */

#ifndef MESHOPT_WRAPPER_H
#define MESHOPT_WRAPPER_H

#include <stddef.h>
#include <stdint.h>

#ifdef _WIN32
    #ifdef MESHOPT_WRAPPER_EXPORTS
        #define MESHOPT_API __declspec(dllexport)
    #else
        #define MESHOPT_API __declspec(dllimport)
    #endif
#else
    #define MESHOPT_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/**
 * Результат симплификации
 */
typedef struct {
    size_t index_count;      // Количество индексов после симплификации
    float result_error;      // Результирующая ошибка
    int success;             // 1 = успех, 0 = ошибка
    char error_message[256]; // Сообщение об ошибке
} MeshoptSimplifyResult;

/**
 * Настройки симплификации с атрибутами
 */
typedef struct {
    // Целевые параметры
    size_t target_index_count;  // Целевое количество индексов (0 = использовать ratio)
    float target_ratio;         // Целевое соотношение (0.0-1.0)
    float target_error;         // Максимальная ошибка (0.0 = без ограничения)

    // Веса атрибутов (UV)
    float uv_weight;            // Вес UV координат (рекомендуется 1.0-2.0)

    // Опции
    int lock_border;            // Блокировать граничные вершины
    int error_is_absolute;      // Ошибка абсолютная (не относительная)
} MeshoptSimplifyOptions;

/**
 * Упрощает меш с сохранением UV атрибутов
 *
 * @param destination       Выходной буфер индексов (должен быть >= index_count)
 * @param indices           Входные индексы
 * @param index_count       Количество входных индексов
 * @param vertex_positions  Позиции вершин (float3 * vertex_count)
 * @param vertex_count      Количество вершин
 * @param vertex_stride     Шаг между вершинами в байтах (обычно sizeof(float)*3)
 * @param vertex_uvs        UV координаты (float2 * vertex_count), может быть NULL
 * @param uv_stride         Шаг между UV в байтах (обычно sizeof(float)*2)
 * @param options           Настройки симплификации
 * @param result            Результат операции
 */
MESHOPT_API void meshopt_simplify_with_uvs(
    unsigned int* destination,
    const unsigned int* indices,
    size_t index_count,
    const float* vertex_positions,
    size_t vertex_count,
    size_t vertex_stride,
    const float* vertex_uvs,
    size_t uv_stride,
    const MeshoptSimplifyOptions* options,
    MeshoptSimplifyResult* result
);

/**
 * Возвращает версию meshoptimizer
 */
MESHOPT_API const char* meshopt_get_version(void);

/**
 * Оптимизирует индексный буфер для vertex cache
 */
MESHOPT_API void meshopt_optimize_vertex_cache_wrap(
    unsigned int* destination,
    const unsigned int* indices,
    size_t index_count,
    size_t vertex_count
);

/**
 * Оптимизирует индексный буфер для overdraw
 */
MESHOPT_API void meshopt_optimize_overdraw_wrap(
    unsigned int* destination,
    const unsigned int* indices,
    size_t index_count,
    const float* vertex_positions,
    size_t vertex_count,
    size_t vertex_stride,
    float threshold
);

#ifdef __cplusplus
}
#endif

#endif // MESHOPT_WRAPPER_H
