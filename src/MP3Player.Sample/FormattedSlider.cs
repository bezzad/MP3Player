using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace MP3Player.Sample
{
    /// <summary>
    /// A Slider which provides a way to modify the 
    /// auto tooltip text by using a format string.
    /// </summary>
    public class FormattedSlider : Slider
    {
        private ToolTip _autoToolTip;
        private ToolTip AutoToolTip
        {
            get
            {
                if (_autoToolTip == null)
                {
                    var field = typeof(Slider).GetField(nameof(_autoToolTip), BindingFlags.NonPublic | BindingFlags.Instance);
                    _autoToolTip = field?.GetValue(this) as ToolTip;
                }

                return _autoToolTip;
            }
        }

        public static readonly DependencyProperty AutoToolTipFormatProperty = DependencyProperty.Register(nameof(AutoToolTipFormat), typeof(string), typeof(FormattedSlider), new PropertyMetadata(default(string)));

        /// <summary>
        /// Gets/sets a format string used to modify the auto tooltip's content.
        /// Note: This format string must contain exactly one placeholder value,
        /// which is used to hold the tooltip's original content.
        /// </summary>
        public string AutoToolTipFormat
        {
            get => (string) GetValue(AutoToolTipFormatProperty);
            set => SetValue(AutoToolTipFormatProperty, value);
        }
        
        protected override void OnThumbDragStarted(DragStartedEventArgs e)
        {
            base.OnThumbDragStarted(e);
            FormatAutoToolTipContent();
        }

        protected override void OnThumbDragDelta(DragDeltaEventArgs e)
        {
            base.OnThumbDragDelta(e);
            FormatAutoToolTipContent();
        }

        private void FormatAutoToolTipContent()
        {
            if (!string.IsNullOrEmpty(AutoToolTipFormat))
            {
                AutoToolTip.Content = string.Format(AutoToolTipFormat, AutoToolTip.Content);
            }
        }
    }
}
