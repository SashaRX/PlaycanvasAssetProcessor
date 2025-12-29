using System.Windows;
using System.Windows.Controls;

namespace AssetProcessor.Controls {
    /// <summary>
    /// UserControl for PlayCanvas project connection.
    /// Exposes ConnectionButtonClicked event for parent window to handle.
    /// </summary>
    public partial class ConnectionPanel : UserControl {
        /// <summary>
        /// Routed event fired when the dynamic connection button is clicked.
        /// </summary>
        public static readonly RoutedEvent ConnectionButtonClickedEvent = EventManager.RegisterRoutedEvent(
            "ConnectionButtonClicked",
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(ConnectionPanel));

        /// <summary>
        /// Event raised when the connection button is clicked.
        /// </summary>
        public event RoutedEventHandler ConnectionButtonClicked {
            add => AddHandler(ConnectionButtonClickedEvent, value);
            remove => RemoveHandler(ConnectionButtonClickedEvent, value);
        }

        public ConnectionPanel() {
            InitializeComponent();
        }

        private void DynamicConnectionButton_Click(object sender, RoutedEventArgs e) {
            // Raise routed event for parent to handle
            RaiseEvent(new RoutedEventArgs(ConnectionButtonClickedEvent, this));
        }
    }
}
