using System;
using System.Collections.Generic;
using System.Windows.Controls;

namespace AssetProcessor.Helpers;

/// <summary>
/// Вспомогательные методы для работы с ComboBox.
/// </summary>
public static class ComboBoxHelper {
    /// <summary>
    /// Заполняет ComboBox перечислением значений типа <typeparamref name="T"/>.
    /// </summary>
    public static void PopulateComboBox<T>(ComboBox comboBox) {
        ArgumentNullException.ThrowIfNull(comboBox);

        var items = new List<string>();
        foreach (object? value in Enum.GetValues(typeof(T))) {
            items.Add(value?.ToString() ?? string.Empty);
        }

        comboBox.ItemsSource = items;
    }
}
