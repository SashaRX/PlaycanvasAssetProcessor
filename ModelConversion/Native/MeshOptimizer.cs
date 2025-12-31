using System.Runtime.InteropServices;

namespace AssetProcessor.ModelConversion.Native {
    /// <summary>
    /// P/Invoke wrapper для meshoptimizer native DLL
    /// Обеспечивает прямой доступ к meshopt_simplifyWithAttributes
    /// для симплификации с сохранением UV seams
    /// </summary>
    public static class MeshOptimizer {
        private const string DllName = "meshopt_wrapper";

        /// <summary>
        /// Результат симплификации
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct SimplifyResult {
            public nuint IndexCount;
            public float ResultError;
            public int Success;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string ErrorMessage;

            public bool IsSuccess => Success != 0;
        }

        /// <summary>
        /// Настройки симплификации
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct SimplifyOptions {
            public nuint TargetIndexCount;
            public float TargetRatio;
            public float TargetError;
            public float UvWeight;
            public int LockBorder;
            public int ErrorIsAbsolute;

            /// <summary>
            /// Создаёт настройки для целевого соотношения треугольников
            /// </summary>
            /// <param name="ratio">Соотношение 0.0-1.0 (0.5 = 50% треугольников)</param>
            /// <param name="uvWeight">Вес UV атрибутов (1.0 = стандартный, 2.0+ = сильнее сохранять UV)</param>
            public static SimplifyOptions FromRatio(float ratio, float uvWeight = 1.0f) {
                return new SimplifyOptions {
                    TargetIndexCount = 0,
                    TargetRatio = Math.Clamp(ratio, 0.01f, 1.0f),
                    TargetError = 0.01f, // 1% максимальная ошибка
                    UvWeight = uvWeight,
                    LockBorder = 0,
                    ErrorIsAbsolute = 0
                };
            }

            /// <summary>
            /// Создаёт настройки для целевого количества треугольников
            /// </summary>
            public static SimplifyOptions FromTargetCount(int targetTriangles, float uvWeight = 1.0f) {
                return new SimplifyOptions {
                    TargetIndexCount = (nuint)(targetTriangles * 3),
                    TargetRatio = 0,
                    TargetError = 0.01f,
                    UvWeight = uvWeight,
                    LockBorder = 0,
                    ErrorIsAbsolute = 0
                };
            }
        }

        /// <summary>
        /// Упрощает меш с сохранением UV атрибутов
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void meshopt_simplify_with_uvs(
            [Out] uint[] destination,
            [In] uint[] indices,
            nuint indexCount,
            [In] float[] vertexPositions,
            nuint vertexCount,
            nuint vertexStride,
            [In] float[]? vertexUvs,
            nuint uvStride,
            in SimplifyOptions options,
            out SimplifyResult result
        );

        /// <summary>
        /// Получает версию meshoptimizer
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr meshopt_get_version();

        /// <summary>
        /// Оптимизирует для vertex cache
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void meshopt_optimize_vertex_cache_wrap(
            [Out] uint[] destination,
            [In] uint[] indices,
            nuint indexCount,
            nuint vertexCount
        );

        /// <summary>
        /// Проверяет доступность native DLL
        /// </summary>
        public static bool IsAvailable() {
            try {
                var version = GetVersion();
                return !string.IsNullOrEmpty(version);
            } catch {
                return false;
            }
        }

        /// <summary>
        /// Получает версию библиотеки
        /// </summary>
        public static string GetVersion() {
            try {
                var ptr = meshopt_get_version();
                return Marshal.PtrToStringAnsi(ptr) ?? "unknown";
            } catch {
                return "unavailable";
            }
        }

        /// <summary>
        /// Упрощает меш с сохранением UV seams
        /// </summary>
        /// <param name="indices">Индексы треугольников</param>
        /// <param name="positions">Позиции вершин (x,y,z для каждой вершины)</param>
        /// <param name="uvs">UV координаты (u,v для каждой вершины), может быть null</param>
        /// <param name="options">Настройки симплификации</param>
        /// <returns>Новые индексы после симплификации</returns>
        public static uint[] SimplifyWithUvs(
            uint[] indices,
            float[] positions,
            float[]? uvs,
            SimplifyOptions options
        ) {
            if (indices == null || indices.Length == 0)
                throw new ArgumentException("Indices array is empty", nameof(indices));

            if (positions == null || positions.Length == 0)
                throw new ArgumentException("Positions array is empty", nameof(positions));

            if (indices.Length % 3 != 0)
                throw new ArgumentException("Index count must be multiple of 3", nameof(indices));

            if (positions.Length % 3 != 0)
                throw new ArgumentException("Position count must be multiple of 3", nameof(positions));

            int vertexCount = positions.Length / 3;
            int indexCount = indices.Length;

            // Проверяем UV
            nuint uvStride = 0;
            if (uvs != null) {
                if (uvs.Length != vertexCount * 2)
                    throw new ArgumentException($"UV count ({uvs.Length / 2}) doesn't match vertex count ({vertexCount})", nameof(uvs));
                uvStride = (nuint)(sizeof(float) * 2);
            }

            // Выделяем буфер для результата (максимум = исходный размер)
            uint[] destination = new uint[indexCount];

            meshopt_simplify_with_uvs(
                destination,
                indices,
                (nuint)indexCount,
                positions,
                (nuint)vertexCount,
                (nuint)(sizeof(float) * 3), // vertex stride
                uvs,
                uvStride,
                in options,
                out SimplifyResult result
            );

            if (!result.IsSuccess) {
                throw new Exception($"Mesh simplification failed: {result.ErrorMessage}");
            }

            // Обрезаем результат до фактического размера
            if ((int)result.IndexCount < indexCount) {
                uint[] trimmed = new uint[(int)result.IndexCount];
                Array.Copy(destination, trimmed, (int)result.IndexCount);
                return trimmed;
            }

            return destination;
        }

        /// <summary>
        /// Оптимизирует индексы для vertex cache
        /// </summary>
        public static uint[] OptimizeVertexCache(uint[] indices, int vertexCount) {
            if (indices == null || indices.Length == 0)
                return indices;

            uint[] destination = new uint[indices.Length];
            meshopt_optimize_vertex_cache_wrap(
                destination,
                indices,
                (nuint)indices.Length,
                (nuint)vertexCount
            );
            return destination;
        }
    }
}
