using System;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MP3Player.Wave.WaveOutputs;
using MP3Player.Wave.WaveProviders;

namespace MP3Player.Sample
{
    public abstract class PlayerViewModel : ViewModel
    {
        protected DispatcherTimer PlayerTimer { get; } = new DispatcherTimer();
        protected IWavePlayer WavePlayer { get; set; }
        protected VolumeWaveProvider16 VolumeProvider { get; set; }
        protected string AppBaseTitle { get; set; } = "MP3 Player";
        public string AppTitle { get; private set; }
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
        public ImageSource TaskbarOverlay { get; set; }
        public ICommand PlayPauseCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand MuteCommand { get; }
        public ICommand OpenFileCommand { get; }
        public ICommand BackwardCommand { get; set; }
        public ICommand ForwardCommand { get; set; }

        protected PlayerViewModel()
        {
            PlayPauseCommand = new RelayCommand(OnPlayPause);
            StopCommand = new RelayCommand(Stop);
            MuteCommand = new RelayCommand(OnMute);
            OpenFileCommand = new RelayCommand(OpenFile);
            ForwardCommand = new RelayCommand(OnForward);
            BackwardCommand = new RelayCommand(OnBackward);
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
        protected abstract void OnBackward();
        protected abstract void OnForward();
        protected abstract void OnPositionChanged();
        protected abstract void OnTick(object sender, EventArgs e);

        protected void OnIsMuteChanged()
        {
            if (VolumeProvider != null)
            {
                VolumeProvider.Volume = IsMute ? 0 : Volume/100;
            }
        }

        protected void OnVolumeChanged()
        {
            if (VolumeProvider != null)
            {
                VolumeProvider.Volume = Volume / 100;
            }
            IsMute = Volume == 0;
        }

        protected void SetTitle(string info)
        {
            AppTitle = AppBaseTitle;
            if (string.IsNullOrWhiteSpace(info) == false)
            {
                AppTitle += $" ({info})";
            }
        }

        public override void Dispose()
        {
            Stop();
            WavePlayer?.Dispose();
        }
    }
}
