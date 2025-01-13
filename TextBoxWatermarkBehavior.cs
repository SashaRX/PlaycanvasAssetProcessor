using Microsoft.Xaml.Behaviors;
using System.Windows;
using System.Windows.Controls;

namespace AssetProcessor {
    public class TextBoxWatermarkBehavior : Behavior<TextBox> {
        public static readonly DependencyProperty WatermarkProperty =
            DependencyProperty.Register(nameof(Watermark), typeof(string), typeof(TextBoxWatermarkBehavior), new PropertyMetadata(default(string)));

        public string Watermark {
            get => (string)GetValue(WatermarkProperty);
            set => SetValue(WatermarkProperty, value);
        }

        protected override void OnAttached() {
            base.OnAttached();
            AssociatedObject.GotFocus += RemoveWatermark;
            AssociatedObject.LostFocus += ShowWatermark;

            if (!string.IsNullOrEmpty(AssociatedObject.Text)) {
                RemoveWatermark(this, EventArgs.Empty);
            } else {
                ShowWatermark(this, EventArgs.Empty);
            }
        }

        protected override void OnDetaching() {
            base.OnDetaching();
            AssociatedObject.GotFocus -= RemoveWatermark;
            AssociatedObject.LostFocus -= ShowWatermark;
        }

        private void RemoveWatermark(object sender, EventArgs e) {
            if (AssociatedObject.Text == Watermark) {
                AssociatedObject.Text = string.Empty;
                AssociatedObject.Foreground = SystemColors.ControlTextBrush;
            }
        }

        private void ShowWatermark(object sender, EventArgs e) {
            if (string.IsNullOrEmpty(AssociatedObject.Text)) {
                AssociatedObject.Text = Watermark;
                AssociatedObject.Foreground = SystemColors.GrayTextBrush;
            } else {
                AssociatedObject.Foreground = SystemColors.ControlTextBrush;
            }
        }
    }
}
