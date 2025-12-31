namespace AssetProcessor.ModelConversion.Core {
    /// <summary>
    /// Расширенные настройки gltfpack CLI
    /// Документация: https://github.com/zeux/meshoptimizer/blob/master/gltf/README.md
    /// </summary>
    public class GltfPackSettings {
        // ============================================
        // SIMPLIFICATION
        // ============================================

        /// <summary>
        /// Максимальная ошибка упрощения (0.0-1.0)
        /// Флаг: -se E
        /// По умолчанию 0.01 = 1% отклонения
        /// </summary>
        public float? SimplificationError { get; set; }

        /// <summary>
        /// Разрешительный режим упрощения
        /// Флаг: -sp
        /// Позволяет упрощение через границы атрибутов (seams)
        /// </summary>
        public bool PermissiveSimplification { get; set; }

        /// <summary>
        /// Блокировать граничные вершины при упрощении
        /// Флаг: -slb
        /// Предотвращает щели на стыках мешей
        /// </summary>
        public bool LockBorderVertices { get; set; }

        // ============================================
        // VERTEX POSITION FORMAT
        // ============================================

        /// <summary>
        /// Формат хранения позиций вершин
        /// </summary>
        public VertexPositionFormat PositionFormat { get; set; } = VertexPositionFormat.Integer;

        // ============================================
        // VERTEX ATTRIBUTES FORMAT
        // ============================================

        /// <summary>
        /// Использовать float для текстурных координат (отключает квантование UV)
        /// Флаг: -vtf
        /// ВАЖНО: Решает проблему денормализации UV при 12-бит квантовании!
        /// </summary>
        public bool FloatTexCoords { get; set; }

        /// <summary>
        /// Использовать float для нормалей (отключает квантование нормалей)
        /// Флаг: -vnf
        /// </summary>
        public bool FloatNormals { get; set; }

        /// <summary>
        /// Использовать interleaved vertex attributes
        /// Флаг: -vi
        /// Уменьшает эффективность сжатия, но может улучшить производительность рендеринга
        /// </summary>
        public bool InterleavedAttributes { get; set; }

        /// <summary>
        /// Сохранять все vertex attributes даже если они не используются
        /// Флаг: -kv
        /// ВАЖНО: Необходим для сохранения UV в моделях без текстур!
        /// </summary>
        public bool KeepVertexAttributes { get; set; } = true;

        // ============================================
        // ANIMATION QUANTIZATION
        // ============================================

        /// <summary>
        /// Биты для квантования translation (перемещение) в анимациях
        /// Флаг: -at N
        /// Диапазон: 1-24, по умолчанию 16
        /// </summary>
        public int AnimationTranslationBits { get; set; } = 16;

        /// <summary>
        /// Биты для квантования rotation в анимациях
        /// Флаг: -ar N
        /// Диапазон: 4-16, по умолчанию 12
        /// </summary>
        public int AnimationRotationBits { get; set; } = 12;

        /// <summary>
        /// Биты для квантования scale в анимациях
        /// Флаг: -as N
        /// Диапазон: 1-24, по умолчанию 16
        /// </summary>
        public int AnimationScaleBits { get; set; } = 16;

        /// <summary>
        /// Частота ресемплинга анимаций (Hz)
        /// Флаг: -af N
        /// По умолчанию 30, 0 = отключить ресемплинг
        /// </summary>
        public int AnimationFrameRate { get; set; } = 30;

        /// <summary>
        /// Сохранять константные animation tracks
        /// Флаг: -ac
        /// </summary>
        public bool KeepConstantAnimationTracks { get; set; }

        // ============================================
        // SCENE OPTIONS
        // ============================================

        /// <summary>
        /// Сохранять именованные ноды для внешней трансформации
        /// Флаг: -kn
        /// </summary>
        public bool KeepNamedNodes { get; set; }

        /// <summary>
        /// Сохранять именованные материалы
        /// Флаг: -km (отключает объединение материалов)
        /// </summary>
        public bool KeepNamedMaterials { get; set; }

        /// <summary>
        /// Сохранять extras данные
        /// Флаг: -ke
        /// </summary>
        public bool KeepExtras { get; set; }

        /// <summary>
        /// Объединять инстансы одинаковых мешей
        /// Флаг: -mm
        /// </summary>
        public bool MergeMeshInstances { get; set; }

        /// <summary>
        /// Использовать EXT_mesh_gpu_instancing для множественных инстансов
        /// Флаг: -mi
        /// </summary>
        public bool UseGpuInstancing { get; set; }

        // ============================================
        // MISCELLANEOUS
        // ============================================

        /// <summary>
        /// Создать сжатый файл с fallback для загрузчиков без поддержки сжатия
        /// Флаг: -cf
        /// </summary>
        public bool CompressedWithFallback { get; set; }

        /// <summary>
        /// Отключить квантование полностью
        /// Флаг: -noq
        /// Создает большие файлы без extensions
        /// </summary>
        public bool DisableQuantization { get; set; }

        /// <summary>
        /// Инвертировать UV по вертикали
        /// Применяется через флаг --flip-uv в gltfpack (если поддерживается)
        /// или через пост-обработку
        /// </summary>
        public bool FlipUVs { get; set; }

        // ============================================
        // FACTORY METHODS
        // ============================================

        /// <summary>
        /// Создает настройки по умолчанию (оптимальные для большинства случаев)
        /// </summary>
        public static GltfPackSettings CreateDefault() {
            return new GltfPackSettings {
                PositionFormat = VertexPositionFormat.Integer,
                FloatTexCoords = false,
                FloatNormals = false,
                KeepVertexAttributes = true,
                LockBorderVertices = false, // Отключено для лучшей симплификации
                AnimationTranslationBits = 16,
                AnimationRotationBits = 12,
                AnimationScaleBits = 16,
                AnimationFrameRate = 30
            };
        }

        /// <summary>
        /// Создает настройки для максимального качества
        /// </summary>
        public static GltfPackSettings CreateHighQuality() {
            return new GltfPackSettings {
                PositionFormat = VertexPositionFormat.Float,
                FloatTexCoords = true,  // Нет квантования UV
                FloatNormals = true,    // Нет квантования нормалей
                KeepVertexAttributes = true,
                LockBorderVertices = false, // Отключено для лучшей симплификации
                AnimationTranslationBits = 24,
                AnimationRotationBits = 16,
                AnimationScaleBits = 24,
                AnimationFrameRate = 60,
                KeepExtras = true,
                KeepNamedNodes = true
            };
        }

        /// <summary>
        /// Создает настройки для минимального размера файла
        /// </summary>
        public static GltfPackSettings CreateMinSize() {
            return new GltfPackSettings {
                PositionFormat = VertexPositionFormat.Integer,
                FloatTexCoords = false,
                FloatNormals = false,
                KeepVertexAttributes = false, // Удалять неиспользуемые атрибуты
                AnimationTranslationBits = 12,
                AnimationRotationBits = 8,
                AnimationScaleBits = 12,
                AnimationFrameRate = 24,
                MergeMeshInstances = true,
                UseGpuInstancing = true
            };
        }

        /// <summary>
        /// Создает настройки совместимые с редакторами (без квантования)
        /// </summary>
        public static GltfPackSettings CreateEditorCompatible() {
            return new GltfPackSettings {
                DisableQuantization = true,
                KeepVertexAttributes = true,
                KeepNamedNodes = true,
                KeepNamedMaterials = true,
                KeepExtras = true
            };
        }
    }

    /// <summary>
    /// Формат хранения позиций вершин
    /// </summary>
    public enum VertexPositionFormat {
        /// <summary>
        /// Integer attributes (по умолчанию)
        /// Флаг: -vpi
        /// </summary>
        Integer,

        /// <summary>
        /// Normalized attributes
        /// Флаг: -vpn
        /// </summary>
        Normalized,

        /// <summary>
        /// Floating point attributes
        /// Флаг: -vpf
        /// </summary>
        Float
    }

    /// <summary>
    /// Источник модели для конвертации
    /// </summary>
    public enum ModelSourceType {
        /// <summary>
        /// FBX файл (требует конвертацию через FBX2glTF)
        /// </summary>
        FBX,

        /// <summary>
        /// GLB/glTF файл (напрямую в gltfpack)
        /// </summary>
        GLB
    }
}
