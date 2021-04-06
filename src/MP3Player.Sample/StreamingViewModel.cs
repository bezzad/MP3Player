using Microsoft.Win32;
using MP3Player.Wave.FileFormats.MP3;
using MP3Player.Wave.WaveFormats;
using MP3Player.Wave.WaveOutputs;
using MP3Player.Wave.WaveProviders;
using MP3Player.Wave.WaveStreams;
using MP3Player.Wave.WinMM;
using PropertyChanged;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MP3Player.Sample
{
    public sealed class StreamingViewModel : PlayerViewModel
    {
        private IWavePlayer _wavePlayer;
        private WaveStream _reader;
        private BufferedWaveProvider _bufferedWaveProvider;
        private VolumeWaveProvider16 _volumeProvider;
        private volatile StreamingPlaybackState _playbackState;
        private volatile bool _fullyDownloaded;
        private string _lastPlayed;
        [AlsoNotifyFor(nameof(SpeedNormal), nameof(SpeedFast), nameof(SpeedFastest))]
        private Speed SpeedState { get; set; } = Speed.Normal;
        private bool IsBufferNearlyFull =>
            _bufferedWaveProvider != null &&
            _bufferedWaveProvider.BufferLength - _bufferedWaveProvider.BufferedBytes
            < _bufferedWaveProvider.WaveFormat.AverageBytesPerSecond / 4;

        public string DefaultDecompressionFormat { get; set; }
        public override bool IsPlaying => _wavePlayer != null && _wavePlayer.PlaybackState == PlaybackState.Playing;
        public override bool IsStopped => _wavePlayer == null || _wavePlayer.PlaybackState == PlaybackState.Stopped;
        public bool SpeedNormal => SpeedState == Speed.Normal;
        public bool SpeedFast => SpeedState == Speed.Fast;
        public bool SpeedFastest => SpeedState == Speed.Fastest;
        public ImageSource TaskbarOverlay { get; set; }
        public ICommand SpeedCommand { get; set; }
        public ICommand BackwardCommand { get; set; }
        public ICommand ForwardCommand { get; set; }

        public StreamingViewModel()
        {
            AppBaseTitle = "Streaming MP3 Player";
            SetTitle("File Not Loaded");
            ForwardCommand = new RelayCommand(OnForward);
            BackwardCommand = new RelayCommand(OnBackward);
            SpeedCommand = new RelayCommand(OnChangedSpeed);
            IsStreaming = true;
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

        private void StreamMp3(string url)
        {
            _fullyDownloaded = false;
            var buffer = new byte[16384 * 4]; // needs to be big enough to hold a decompressed frame
            IMp3FrameDecompressor deCompressor = null;

            try
            {
                var webRequest = (HttpWebRequest)WebRequest.Create(url);
                var resp = (HttpWebResponse)webRequest.GetResponse();
                using var responseStream = resp.GetResponseStream();
                var readFullyStream = new ReadFullyStream(responseStream);

                do
                {
                    if (IsBufferNearlyFull)
                    {
                        Debug.WriteLine("Buffer getting full, taking a break");
                        Thread.Sleep(500);
                    }
                    else
                    {
                        Mp3Frame frame;
                        try
                        {
                            frame = Mp3Frame.LoadFromStream(readFullyStream);
                        }
                        catch (EndOfStreamException)
                        {
                            _fullyDownloaded = true;
                            // reached the end of the MP3 file / stream
                            break;
                        }
                        catch (WebException)
                        {
                            // probably we have aborted download from the GUI thread
                            break;
                        }

                        if (frame == null)
                            break;
                        if (deCompressor == null)
                        {
                            // don't think these details matter too much - just help ACM select the right codec
                            // however, the buffered provider doesn't know what sample rate it is working at
                            // until we have a frame
                            deCompressor = CreateFrameDeCompressor(frame);
                            _bufferedWaveProvider = new BufferedWaveProvider(deCompressor.OutputFormat) {
                                // allow us to get well ahead of ourselves
                                BufferDuration = TimeSpan.FromSeconds(20)
                            };

                            //this.bufferedWaveProvider.BufferedDuration = 250;
                        }

                        int decompressed = deCompressor.DecompressFrame(frame, buffer, 0);
                        //Debug.WriteLine(String.Format("Decompressed a frame {0}", decompressed));
                        _bufferedWaveProvider.AddSamples(buffer, 0, decompressed);
                    }

                } while (_playbackState != StreamingPlaybackState.Stopped);

                Debug.WriteLine("Exiting");
                // was doing this in a finally block, but for some reason
                // we are hanging on response stream .Dispose so never get there
                deCompressor?.Dispose();
            }
            catch (WebException e)
            {
                if (e.Status != WebExceptionStatus.RequestCanceled)
                {
                    Dispatcher.CurrentDispatcher.BeginInvoke(() => MessageBox.Show(e.Message));
                }
            }
            catch (Exception e)
            {
                Dispatcher.CurrentDispatcher.BeginInvoke(() => MessageBox.Show(e.Message));
            }
            finally
            {
                deCompressor?.Dispose();
            }
        }
        private static IMp3FrameDecompressor CreateFrameDeCompressor(Mp3Frame frame)
        {
            WaveFormat waveFormat =
                new Mp3WaveFormat(frame.SampleRate, frame.ChannelMode == ChannelMode.Mono ? 1 : 2, frame.FrameLength, frame.BitRate);
            return new AcmMp3FrameDecompressor(waveFormat);
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

        protected override void Pause()
        {
            if (InputPath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                _playbackState = StreamingPlaybackState.Buffering;
            }
            else
            {
                _wavePlayer?.Pause();
            }

            UpdatePlayerState();
            PlayerTimer?.Stop();
            TaskbarOverlay = (ImageSource)Application.Current.FindResource("PauseImage");
            SetTitle("Pause " + Path.GetFileName(InputPath));
        }

        protected override void Play()
        {
            if (string.IsNullOrEmpty(InputPath))
            {
                MessageBox.Show("Select a valid input file or URL first");
                return;
            }

            if (InputPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)) // streaming play
            {
                _playbackState = StreamingPlaybackState.Buffering;
                _bufferedWaveProvider = null;
                Task.Run(() => StreamMp3(InputPath));
            }
            else // play from file
            {
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
            }

            UpdatePlayerState();
            PlayerTimer.Start();
            TaskbarOverlay = (ImageSource)Application.Current.FindResource("PlayImage");
            SetTitle("Playing " + Path.GetFileName(InputPath));
        }

        protected override void Stop()
        {
            _wavePlayer?.Stop();
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

        protected override void OnVolumeChanged()
        {
            if (_volumeProvider != null)
            {
                _volumeProvider.Volume = Volume / 100;
            }
            IsMute = Volume == 0;
        }

        protected override void OnIsMuteChanged()
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

        public override void Dispose()
        {
            _wavePlayer?.Dispose();
            _reader?.Dispose();
        }
    }
}
