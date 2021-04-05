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
    }
}
