﻿using Microsoft.Win32;
using MP3Player.Wave.WaveOutputs;
using MP3Player.Wave.WaveProviders;
using MP3Player.Wave.WaveStreams;
using MP3Player.Wave.WinMM;
using System;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace MP3Player.Sample
{
    public sealed class SimpleMp3PlayerViewModel : PlayerViewModel
    {
        private WaveStream _reader;
        private string _lastPlayed;

        public string DefaultDecompressionFormat { get; set; }
        public override bool IsPlaying => WavePlayer != null && WavePlayer.PlaybackState == PlaybackState.Playing;
        public override bool IsStopped => WavePlayer == null || WavePlayer.PlaybackState == PlaybackState.Stopped;

        public SimpleMp3PlayerViewModel()
        {
            AppBaseTitle = "Simple MP3 Player";
            SetTitle("File Not Loaded");
            IsStreaming = false;
        }

        protected override void OnBackward()
        {
            if (_reader != null)
            {
                _reader.Position = Math.Max(_reader.Position - (long)_reader.WaveFormat.AverageBytesPerSecond * 10, 0);
                OnPropertyChanged(nameof(Position));
            }
        }

        protected override void OnForward()
        {
            if (_reader != null)
            {
                _reader.Position = Math.Min(_reader.Position + (long)_reader.WaveFormat.AverageBytesPerSecond * 10, _reader.Length-1);
                OnPropertyChanged(nameof(Position));
            }
        }

        protected override void Pause()
        {
            WavePlayer?.Pause();
            UpdatePlayerState();
            PlayerTimer?.Stop();
            TaskbarOverlay = (ImageSource)Application.Current.FindResource("PauseImage");
            SetTitle("Pause " + Path.GetFileName(InputPath));
        }

        protected override void OpenFile()
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
                    SetTitle(Path.GetFileName(InputPath));
                }
            }
            catch (Exception e)
            {
                MessageBox.Show($"Not a supported input file ({e.Message})");
            }
        }

        protected override void Play()
        {
            if (string.IsNullOrEmpty(InputPath) || File.Exists(InputPath) == false)
            {
                MessageBox.Show("Select a valid input file or URL first");
                return;
            }
            if (WavePlayer == null)
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
                VolumeProvider = new VolumeWaveProvider16(_reader) { Volume = Volume / 100 };
                _lastPlayed = InputPath;
                WavePlayer.Init(VolumeProvider);
                Duration = _reader.TotalTime;
            }
            WavePlayer.Play();
            UpdatePlayerState();
            PlayerTimer.Start();
            TaskbarOverlay = (ImageSource)Application.Current.FindResource("PlayImage");
            SetTitle("Playing " + Path.GetFileName(InputPath));
        }

        protected override void Stop()
        {
            WavePlayer?.Stop();
            TaskbarOverlay = null;
            PlayerTimer?.Stop();
            Position = 0;
            SetTitle(Path.GetFileName(InputPath));
        }

        protected override void OnTick(object sender, EventArgs eventArgs)
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

        protected override void OnPositionChanged()
        {
            if (_reader != null)
            {
                _reader.Position = (long)(_reader.Length * Position / MaxPosition);
                CurrentTime = _reader.CurrentTime;
                OnPropertyChanged(nameof(PositionPercent));
            }
        }

        private void CreatePlayer()
        {
            WavePlayer = new WaveOutEvent();
            WavePlayer.PlaybackStopped += OnPlaybackStopped;
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

        public override void Dispose()
        {
            base.Dispose();
            _reader?.Dispose();
        }
    }
}
