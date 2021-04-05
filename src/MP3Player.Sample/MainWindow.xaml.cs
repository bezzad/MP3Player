using System.Windows;

namespace MP3Player.Sample
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new SimpleMp3PlayerViewModel();
        }

        private void OnSimple(object sender, RoutedEventArgs e)
        {
            DataContext = new SimpleMp3PlayerViewModel();
        }

        private void OnStreaming(object sender, RoutedEventArgs e)
        {
             DataContext= new StreamingViewModel();
        }
    }
}
