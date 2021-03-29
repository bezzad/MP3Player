using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using MP3Player.Wave;

namespace MP3Player.Sample
{
    public class ViewModel : INotifyPropertyChanged
    {
        private IWavePlayer wavePlayer;
        public event PropertyChangedEventHandler PropertyChanged;
        public double Position { get; set; }
        public int Volume { get; set; }
        public bool IsPlaying { get; set; }
        public ICommand PlayPauseCommand { get; set; }
        public ICommand NextCommand { get; set; }
        public ICommand PreviousCommand { get; set; }

        public ViewModel()
        {
            PlayPauseCommand = new RelayCommandAsync(OnPlayPause);
        }

        private Task OnPlayPause()
        {
            return IsPlaying ? Pause() : Play();
        }

        private Task Pause()
        {
            IsPlaying = false;
            return Task.CompletedTask;
        }

        private Task Play()
        {
            IsPlaying = true;
            return Task.CompletedTask;
        }

        private void OnPositionChanged()
        {

        }

        private void OnVolumeChanged()
        {

        }

    }
}
