namespace AssetProcessor.Infrastructure.Enums;

/// <summary>
/// Канал цвета, используемый в материалах и текстурах.
/// </summary>
public enum ColorChannel {
    /// <summary>
    /// Использовать все каналы RGB.
    /// </summary>
    RGB,

    /// <summary>
    /// Использовать только красный канал.
    /// </summary>
    R,

    /// <summary>
    /// Использовать только зелёный канал.
    /// </summary>
    G,

    /// <summary>
    /// Использовать только синий канал.
    /// </summary>
    B,

    /// <summary>
    /// Использовать только альфа-канал.
    /// </summary>
    A,
}
