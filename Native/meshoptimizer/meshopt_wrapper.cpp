/**
 * meshopt_wrapper.cpp - Implementation of meshoptimizer wrapper
 *
 * Uses meshopt_simplifyWithAttributes for UV-preserving mesh simplification.
 */

#define MESHOPT_WRAPPER_EXPORTS
#include "meshopt_wrapper.h"
#include "meshoptimizer.h"

#include <cstring>
#include <algorithm>
#include <cmath>

// Version string
static const char* WRAPPER_VERSION = "meshoptimizer 0.21 wrapper v1.0";

extern "C" {

MESHOPT_API const char* meshopt_get_version(void) {
    return WRAPPER_VERSION;
}

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
) {
    // Инициализация результата
    result->index_count = 0;
    result->result_error = 0.0f;
    result->success = 0;
    result->error_message[0] = '\0';

    // Валидация входных данных
    if (!destination || !indices || !vertex_positions || !options || !result) {
        strncpy(result->error_message, "Null pointer in required parameter", sizeof(result->error_message) - 1);
        return;
    }

    if (index_count == 0 || vertex_count == 0) {
        strncpy(result->error_message, "Empty mesh (zero indices or vertices)", sizeof(result->error_message) - 1);
        return;
    }

    if (index_count % 3 != 0) {
        strncpy(result->error_message, "Index count must be multiple of 3", sizeof(result->error_message) - 1);
        return;
    }

    // Вычисляем целевое количество индексов
    size_t target_index_count = options->target_index_count;
    if (target_index_count == 0 && options->target_ratio > 0.0f) {
        target_index_count = static_cast<size_t>(index_count * options->target_ratio);
        // Округляем до кратного 3
        target_index_count = (target_index_count / 3) * 3;
    }

    if (target_index_count == 0) {
        target_index_count = 3; // Минимум 1 треугольник
    }

    // Настройки meshoptimizer
    unsigned int meshopt_options = 0;
    if (options->lock_border) {
        meshopt_options |= meshopt_SimplifyLockBorder;
    }
    if (options->error_is_absolute) {
        meshopt_options |= meshopt_SimplifyErrorAbsolute;
    }

    float result_error = 0.0f;

    // Выбираем метод симплификации
    size_t new_index_count;

    if (vertex_uvs != nullptr && options->uv_weight > 0.0f) {
        // Симплификация С атрибутами (UV)
        // meshopt_simplifyWithAttributes требует массив атрибутов и весов

        // Подготавливаем атрибуты (UV.x, UV.y)
        const size_t attribute_count = 2; // U и V как отдельные атрибуты
        float attribute_weights[2] = { options->uv_weight, options->uv_weight };

        // meshopt_simplifyWithAttributes ожидает vertex_attributes как указатель на первый атрибут
        // и attribute_stride как шаг между атрибутами в байтах

        new_index_count = meshopt_simplifyWithAttributes(
            destination,
            indices,
            index_count,
            vertex_positions,
            vertex_count,
            vertex_stride,
            vertex_uvs,           // Указатель на UV данные
            uv_stride,            // Шаг между UV (sizeof(float)*2)
            attribute_weights,    // Веса для U и V
            attribute_count,      // Количество атрибутов (2 для UV)
            nullptr,              // vertex_lock (nullptr = не блокировать отдельные вершины)
            target_index_count,
            options->target_error,
            meshopt_options,
            &result_error
        );
    } else {
        // Базовая симплификация БЕЗ атрибутов
        new_index_count = meshopt_simplify(
            destination,
            indices,
            index_count,
            vertex_positions,
            vertex_count,
            vertex_stride,
            target_index_count,
            options->target_error,
            meshopt_options,
            &result_error
        );
    }

    result->index_count = new_index_count;
    result->result_error = result_error;
    result->success = 1;
}

MESHOPT_API void meshopt_optimize_vertex_cache_wrap(
    unsigned int* destination,
    const unsigned int* indices,
    size_t index_count,
    size_t vertex_count
) {
    meshopt_optimizeVertexCache(destination, indices, index_count, vertex_count);
}

MESHOPT_API void meshopt_optimize_overdraw_wrap(
    unsigned int* destination,
    const unsigned int* indices,
    size_t index_count,
    const float* vertex_positions,
    size_t vertex_count,
    size_t vertex_stride,
    float threshold
) {
    meshopt_optimizeOverdraw(destination, indices, index_count, vertex_positions, vertex_count, vertex_stride, threshold);
}

} // extern "C"
