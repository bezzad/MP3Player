using Microsoft.Win32;
using MP3Player.Wave.WaveOutputs;
using MP3Player.Wave.WaveStreams;
using MP3Player.Wave.WinMM;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace MP3Player.Sample
{
    public class ViewModel : INotifyPropertyChanged
    {
        private IWavePlayer wavePlayer;
        private WaveStream reader;
        private readonly DispatcherTimer timer = new DispatcherTimer();
        private string lastPlayed;

        public event PropertyChangedEventHandler PropertyChanged;
        public double Position { get; set; }
        public double MaxPosition { get; set; } = 1000;
        public int Volume { get; set; }
        public string InputPath { get; set; }
        public string DefaultDecompressionFormat { get; set; }
        public bool IsPlaying => wavePlayer != null && wavePlayer.PlaybackState == PlaybackState.Playing;
        public bool IsStopped => wavePlayer == null || wavePlayer.PlaybackState == PlaybackState.Stopped;
        public ICommand OpenFilesCommand { get; set; }
        public ICommand PlayPauseCommand { get; set; }
        public ICommand NextCommand { get; set; }
        public ICommand PreviousCommand { get; set; }

        public ViewModel()
        {
            OpenFilesCommand = new RelayCommand(OnOpenFiles);
            PlayPauseCommand = new RelayCommand(OnPlayPause);
            timer.Interval = TimeSpan.FromMilliseconds(500);
            timer.Tick += TimerOnTick;
        }

        private void OnOpenFiles()
        {
            var ofd = new OpenFileDialog();
            if (ofd.ShowDialog() == true)
            {
                if (TryOpenInputFile(ofd.FileName))
                {
                    TryOpenInputFile(ofd.FileName);
                }
            }
        }

        private bool TryOpenInputFile(string file)
        {
            bool isValid = false;
            try
            {
                Stop();
                using var tempReader = new Mp3FileReader(file);
                DefaultDecompressionFormat = tempReader.WaveFormat.ToString();
                InputPath = file;
                isValid = true;
            }
            catch (Exception e)
            {
                MessageBox.Show($"Not a supported input file ({e.Message})");
            }
            return isValid;
        }

        private void TimerOnTick(object sender, EventArgs eventArgs)
        {
            if (reader != null)
            {
                Position = Math.Min(MaxPosition, reader.Position * MaxPosition / reader.Length);
                OnPropertyChanged(nameof(Position));
            }
        }

        private void OnPlayPause()
        {
            if (IsPlaying)
                Pause();
            else
                Play();
        }

        private void Pause()
        {
            wavePlayer?.Pause();
            UpdatePlayerState();
        }

        private void Play()
        {
            if (String.IsNullOrEmpty(InputPath))
            {
                MessageBox.Show("Select a valid input file or URL first");
                return;
            }
            if (wavePlayer == null)
            {
                CreatePlayer();
            }
            if (lastPlayed != InputPath && reader != null)
            {
                reader.Dispose();
                reader = null;
            }
            if (reader == null)
            {
                reader = new Mp3FileReader(InputPath);
                lastPlayed = InputPath;
                wavePlayer.Init(reader);
            }
            wavePlayer.Play();
            UpdatePlayerState();
            timer.Start();
        }

        private void Stop()
        {
            wavePlayer?.Stop();
        }

        private void UpdatePlayerState()
        {
            OnPropertyChanged(nameof(IsPlaying));
            OnPropertyChanged(nameof(IsStopped));
        }

        private void OnPositionChanged()
        {
            if (reader != null)
            {
                reader.Position = (long)(reader.Length * Position / MaxPosition);
            }
        }

        private void OnVolumeChanged()
        {

        }

        private void CreatePlayer()
        {
            wavePlayer = new WaveOutEvent();
            wavePlayer.PlaybackStopped += WavePlayerOnPlaybackStopped;
        }

        private void WavePlayerOnPlaybackStopped(object sender, StoppedEventArgs stoppedEventArgs)
        {
            if (reader != null)
            {
                Position = 0;
                timer.Stop();
            }
            if (stoppedEventArgs.Exception != null)
            {
                MessageBox.Show(stoppedEventArgs.Exception.Message, "Error Playing File");
            }

            OnPropertyChanged(nameof(IsPlaying));
            OnPropertyChanged(nameof(IsStopped));
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            wavePlayer?.Dispose();
            reader?.Dispose();
        }
    }
}
