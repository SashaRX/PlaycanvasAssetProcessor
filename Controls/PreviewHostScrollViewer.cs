using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AssetProcessor.Controls;

public class PreviewHostScrollViewer : ScrollViewer
{
    public static readonly DependencyProperty ScrollBlockElementProperty =
        DependencyProperty.Register(
            nameof(ScrollBlockElement),
            typeof(UIElement),
            typeof(PreviewHostScrollViewer),
            new PropertyMetadata(null));

    public UIElement? ScrollBlockElement
    {
        get => (UIElement?)GetValue(ScrollBlockElementProperty);
        set => SetValue(ScrollBlockElementProperty, value);
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        if (ScrollBlockElement != null && IsDescendantOfBlockElement(e.OriginalSource as DependencyObject))
        {
            // Пропускаем вызов базовой логики ScrollViewer, чтобы колесо мыши не прокручивало контейнер.
            // Событие продолжит туннелировать вниз до ScrollBlockElement, где обработчик зума возьмёт управление.
            return;
        }

        base.OnPreviewMouseWheel(e);
    }

    private bool IsDescendantOfBlockElement(DependencyObject? source)
    {
        while (source != null)
        {
            if (ReferenceEquals(source, ScrollBlockElement))
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }
}
