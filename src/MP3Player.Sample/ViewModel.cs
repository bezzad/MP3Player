﻿using Microsoft.Win32;
using MP3Player.Wave.WaveOutputs;
using MP3Player.Wave.WaveProviders;
using MP3Player.Wave.WaveStreams;
using MP3Player.Wave.WinMM;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PropertyChanged;

namespace MP3Player.Sample
{
    public sealed class ViewModel : INotifyPropertyChanged, IDisposable
    {
        private IWavePlayer _wavePlayer;
        private WaveStream _reader;
        private VolumeWaveProvider16 _volumeProvider;
        private readonly DispatcherTimer _timer = new DispatcherTimer();
        private string _lastPlayed;
        public bool IsStreaming { get; set; }
        [AlsoNotifyFor(nameof(SpeedNormal), nameof(SpeedFast), nameof(SpeedFastest))]
        private Speed SpeedState { get; set; } = Speed.Normal;

        public event PropertyChangedEventHandler PropertyChanged;
        public double PositionPercent => (double)(_reader?.Position ?? 0) / (_reader?.Length ?? 1);
        public double Position { get; set; }
        public double MaxPosition { get; set; } = 1000;
        public float Volume { get; set; } = 100;
        public TimeSpan Duration { get; set; }
        public TimeSpan CurrentTime { get; set; }
        public string AppTitle { get; set; }
        public string InputPath { get; set; }
        public string DefaultDecompressionFormat { get; set; }
        public bool IsPlaying => _wavePlayer != null && _wavePlayer.PlaybackState == PlaybackState.Playing;
        public bool IsStopped => _wavePlayer == null || _wavePlayer.PlaybackState == PlaybackState.Stopped;
        public bool IsMute { get; set; }
        public bool SpeedNormal => SpeedState == Speed.Normal;
        public bool SpeedFast => SpeedState == Speed.Fast;
        public bool SpeedFastest => SpeedState == Speed.Fastest;
        public ImageSource TaskbarOverlay { get; set; }
        public ICommand OpenStreamingCommand { get; set; }
        public ICommand OpenFilesCommand { get; set; }
        public ICommand PlayPauseCommand { get; set; }
        public ICommand StopCommand { get; set; }
        public ICommand TimerCommand { get; set; }
        public ICommand SpeedCommand { get; set; }
        public ICommand MuteCommand { get; set; }
        public ICommand BackwardCommand { get; set; }
        public ICommand ForwardCommand { get; set; }

        public ViewModel()
        {
            AppTitle = "Simple MP3 Player  (File Not Loaded)";
            OpenStreamingCommand = new RelayCommand(OnStreaming);
            OpenFilesCommand = new RelayCommand(OnOpenFiles);
            PlayPauseCommand = new RelayCommand(OnPlayPause);
            StopCommand = new RelayCommand(Stop);
            MuteCommand = new RelayCommand(OnMute);
            ForwardCommand = new RelayCommand(OnForward);
            BackwardCommand = new RelayCommand(OnBackward);
            SpeedCommand = new RelayCommand(OnChangedSpeed);
            _timer.Interval = TimeSpan.FromMilliseconds(500);
            _timer.Tick += Tick;
        }

        private void OnChangedSpeed()
        {
            switch (SpeedState)
            {
                case Speed.Normal:
                    SpeedState = Speed.Fast;
                    break;
                case Speed.Fast:
                    SpeedState = Speed.Fastest;
                    break;
                case Speed.Fastest:
                    SpeedState = Speed.Normal;
                    break;
            }
        }

        private void OnBackward()
        {
            if (_reader != null)
            {
                _reader.Position = Math.Max(_reader.Position - (long)_reader.WaveFormat.AverageBytesPerSecond * 10, 0);
                OnPropertyChanged(nameof(Position));
            }
        }

        private void OnForward()
        {
            if (_reader != null)
            {
                _reader.Position = Math.Min(_reader.Position + (long)_reader.WaveFormat.AverageBytesPerSecond * 10, _reader.Length-1);
                OnPropertyChanged(nameof(Position));
            }
        }

        private void OnStreaming()
        {
            IsStreaming = true;
        }

        private void OnOpenFiles()
        {
            try
            {
                var ofd = new OpenFileDialog { Filter = "MP3 files (*.mp3)|*.mp3" };
                if (ofd.ShowDialog() == true)
                {
                    Stop();
                    using var tempReader = new Mp3FileReader(ofd.FileName);
                    DefaultDecompressionFormat = tempReader.WaveFormat.ToString();
                    InputPath = ofd.FileName;
                    IsStreaming = false;
                    AppTitle = $"Simple MP3 Player  ({Path.GetFileName(InputPath)})";
                }
            }
            catch (Exception e)
            {
                MessageBox.Show($"Not a supported input file ({e.Message})");
            }
        }

        private void OnMute()
        {
            IsMute = !IsMute;
        }

        private void OnPlayPause()
        {
            if (string.IsNullOrWhiteSpace(InputPath))
            {
                OnOpenFiles();
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

        private void Pause()
        {
            _wavePlayer?.Pause();
            UpdatePlayerState();
            _timer?.Stop();
            TaskbarOverlay = (ImageSource)Application.Current.FindResource("PauseImage");
            AppTitle = $"Simple MP3 Player  (Pause {Path.GetFileName(InputPath)})";
        }

        private void Play()
        {
            if (string.IsNullOrEmpty(InputPath))
            {
                MessageBox.Show("Select a valid input file or URL first");
                return;
            }
            if (_wavePlayer == null)
            {
                CreatePlayer();
            }
            if (_lastPlayed != InputPath && _reader != null)
            {
                _reader.Dispose();
                _reader = null;
            }
            if (_reader == null)
            {
                _reader = new Mp3FileReader(InputPath);
                _volumeProvider = new VolumeWaveProvider16(_reader) { Volume = Volume / 100 };
                _lastPlayed = InputPath;
                _wavePlayer.Init(_volumeProvider);
                Duration = _reader.TotalTime;
            }
            _wavePlayer.Play();
            UpdatePlayerState();
            _timer.Start();
            TaskbarOverlay = (ImageSource)Application.Current.FindResource("PlayImage");
            AppTitle = $"Simple MP3 Player  (Playing {Path.GetFileName(InputPath)})";
        }

        private void Stop()
        {
            _wavePlayer?.Stop();
            TaskbarOverlay = null;
            _timer?.Stop();
            Position = 0;
            AppTitle = $"Simple MP3 Player  ({Path.GetFileName(InputPath)})";
        }

        private void Tick(object sender, EventArgs eventArgs)
        {
            if (_reader != null)
            {
                Position = Math.Min(MaxPosition, _reader.Position * MaxPosition / _reader.Length);
                OnPropertyChanged(nameof(PositionPercent));
            }
        }

        private void UpdatePlayerState()
        {
            OnPropertyChanged(nameof(IsPlaying));
            OnPropertyChanged(nameof(IsStopped));
        }

        private void OnPositionChanged()
        {
            if (_reader != null)
            {
                _reader.Position = (long)(_reader.Length * Position / MaxPosition);
                CurrentTime = _reader.CurrentTime;
                OnPropertyChanged(nameof(PositionPercent));
            }
        }

        private void OnVolumeChanged()
        {
            if (_volumeProvider != null)
            {
                _volumeProvider.Volume = Volume / 100;
            }
            IsMute = Volume == 0;
        }

        private void OnIsMuteChanged()
        {
            if (_volumeProvider != null)
            {
                _volumeProvider.Volume = IsMute ? 0 : Volume/100;
            }
        }

        private void CreatePlayer()
        {
            _wavePlayer = new WaveOutEvent();
            _wavePlayer.PlaybackStopped += OnPlaybackStopped;
        }

        private void OnPlaybackStopped(object sender, StoppedEventArgs stoppedEventArgs)
        {
            if (_reader != null)
            {
                Stop();
            }
            if (stoppedEventArgs.Exception != null)
            {
                MessageBox.Show(stoppedEventArgs.Exception.Message, "Error Playing File");
            }

            OnPropertyChanged(nameof(IsPlaying));
            OnPropertyChanged(nameof(IsStopped));
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            _wavePlayer?.Dispose();
            _reader?.Dispose();
        }
    }
}
