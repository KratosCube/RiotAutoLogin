using RiotAutoLogin.Models;
using System.Windows;

namespace RiotAutoLogin.Controls
{
    public partial class QueueSelector : System.Windows.Controls.UserControl
    {
        public static readonly DependencyProperty SelectedQueueProperty = DependencyProperty.Register(
            nameof(SelectedQueue), typeof(RankedQueue), typeof(QueueSelector),
            new FrameworkPropertyMetadata(RankedQueue.SoloDuo, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public RankedQueue SelectedQueue
        {
            get => (RankedQueue)GetValue(SelectedQueueProperty);
            set => SetValue(SelectedQueueProperty, value);
        }

        public QueueSelector() => InitializeComponent();
    }
}
