using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MP3Player.Wasapi.CoreAudioApi;
using MP3Player.Wave.WaveOutputs;
using MP3Player.Wave.WaveProviders;
using MP3Player.Wave.WinMM;

namespace MP3Player.Sample
{
    public abstract class PlayerViewModel : ViewModel
    {
        protected volatile bool PositionChanging;
        protected MMDevice CurrentDevice { get; set; }
        protected DispatcherTimer PlayerTimer { get; } = new DispatcherTimer();
        protected IWavePlayer WavePlayer { get; set; }
        protected VolumeWaveProvider16 VolumeProvider { get; set; }
        protected string AppBaseTitle { get; set; } = "MP3 Player";
        public string AppTitle { get; private set; }
        public string InputPath { get; set; }
        public float Volume { get; set; } = 100;
        public TimeSpan Duration { get; set; }
        public TimeSpan CurrentTime { get; set; }
        public long Position { get; set; }
        public long MaxPosition { get; set; } = 1000;
        public double PositionPercent => (double)Position / MaxPosition;
        public bool IsMute { get; set; }
        public bool IsStreaming { get; set; }
        public bool IsPlaying => WavePlayer != null && WavePlayer.PlaybackState == PlaybackState.Playing;
        public bool IsStopped => WavePlayer == null || WavePlayer.PlaybackState == PlaybackState.Stopped;
        public ImageSource TaskbarOverlay { get; set; }
        public ICommand PlayPauseCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand MuteCommand { get; }
        public ICommand OpenFileCommand { get; }
        public ICommand BackwardCommand { get; }
        public ICommand ForwardCommand { get; }

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
            SetCurrentDevice();
        }

        private void SetCurrentDevice()
        {
            var deviceEnumerator = new MMDeviceEnumerator();
            var devices = deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            CurrentDevice = devices.FirstOrDefault();
            if (CurrentDevice != null)
            {
                CurrentDevice.AudioEndpointVolume.OnVolumeNotification += AudioEndpointVolumeNotification;
            }
        }

        protected virtual void AudioEndpointVolumeNotification(AudioVolumeNotificationData data)
        {
            if (data.Muted)
            {
                Pause();
            }

            Volume = CurrentDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100;
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

        protected void CreatePlayer()
        {
            WavePlayer = new WaveOutEvent();
            WavePlayer.PlaybackStopped += OnPlaybackStopped;
        }

        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            Stop();
            if (e.Exception != null)
            {
                MessageBox.Show(e.Exception.Message, "Error Playing File");
            }
            UpdatePlayerState();
        }

        protected virtual void UpdatePlayerState()
        {
            OnPropertyChanged(nameof(IsPlaying));
            OnPropertyChanged(nameof(IsStopped));
        }

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
            CurrentDevice.AudioEndpointVolume.OnVolumeNotification -= AudioEndpointVolumeNotification;
            CurrentDevice.Dispose();
        }
    }
}
