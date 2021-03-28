using System.ComponentModel;
using System.Windows.Input;

namespace MP3Player.Sample
{
    public class ViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public double Position { get; set; }
        public int Volume { get; set; }
        public bool IsPlaying { get; set; }
        public ICommand PlayPauseCommand { get; set; }
        public ICommand NextCommand { get; set; }
        public ICommand PreviousCommand { get; set; }
    }
}
