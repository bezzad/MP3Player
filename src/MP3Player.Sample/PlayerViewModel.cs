using System;
using System.Windows.Input;
using System.Windows.Threading;

namespace MP3Player.Sample
{
    public abstract class PlayerViewModel : ViewModel
    {
        protected DispatcherTimer PlayerTimer { get; } = new DispatcherTimer();

        public string AppTitle { get; set; }
        public string InputPath { get; set; }
        public float Volume { get; set; } = 100;
        public TimeSpan Duration { get; set; }
        public TimeSpan CurrentTime { get; set; }
        public double Position { get; set; }
        public double MaxPosition { get; set; } = 1000;
        public double PositionPercent => Position / MaxPosition;
        public bool IsMute { get; set; }
        public bool IsStreaming { get; set; }
        public abstract bool IsPlaying { get; }
        public abstract bool IsStopped { get; }
        public ICommand PlayPauseCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand MuteCommand { get; }
        public ICommand OpenFileCommand { get; }

        protected PlayerViewModel()
        {
            PlayPauseCommand = new RelayCommand(OnPlayPause);
            StopCommand = new RelayCommand(Stop);
            MuteCommand = new RelayCommand(OnMute);
            OpenFileCommand = new RelayCommand(OpenFile);
            PlayerTimer.Interval = TimeSpan.FromMilliseconds(500);
            PlayerTimer.Tick += OnTick;
        }

        private void OnPlayPause()
        {
            if (string.IsNullOrWhiteSpace(InputPath))
            {
                OpenFile();
                Play();
            }
            else
            {
                if (IsPlaying)
                    Pause();
                else
                    Play();
            }
        }
        private void OnMute()
        {
            IsMute = !IsMute;
        }

        protected abstract void Stop();
        protected abstract void Play();
        protected abstract void Pause();
        protected abstract void OpenFile();
        protected abstract void OnPositionChanged();
        protected abstract void OnIsMuteChanged();
        protected abstract void OnVolumeChanged();
        protected abstract void OnTick(object sender, EventArgs e);

        protected virtual void SetTitle(string info)
        {
            AppTitle = "MP3 Player";
            if (string.IsNullOrWhiteSpace(info) == false)
            {
                AppTitle += $" ({info})";
            }
        }
    }
}
