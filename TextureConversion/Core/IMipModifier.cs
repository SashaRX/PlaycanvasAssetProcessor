using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AssetProcessor.TextureConversion.Core {
    /// <summary>
    /// Интерфейс для постобработки мипмапов
    /// Используется для реализации специальных алгоритмов (например, Toksvig gloss)
    /// </summary>
    public interface IMipModifier {
        /// <summary>
        /// Название модификатора
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Применяет модификацию к мипмапу
        /// </summary>
        /// <param name="mipImage">Изображение мипмапа для модификации</param>
        /// <param name="mipLevel">Уровень мипмапа (0 = базовый уровень)</param>
        /// <param name="originalImage">Оригинальное изображение (может быть null)</param>
        /// <returns>Модифицированное изображение</returns>
        Image<Rgba32> Apply(Image<Rgba32> mipImage, int mipLevel, Image<Rgba32>? originalImage = null);

        /// <summary>
        /// Проверяет, применим ли модификатор к данному типу текстуры
        /// </summary>
        /// <param name="textureType">Тип текстуры</param>
        /// <returns>True если модификатор применим</returns>
        bool IsApplicable(TextureType textureType);
    }
}
