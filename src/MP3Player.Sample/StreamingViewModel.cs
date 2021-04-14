using Microsoft.Win32;
using MP3Player.Wave.FileFormats.MP3;
using MP3Player.Wave.WaveFormats;
using MP3Player.Wave.WaveOutputs;
using MP3Player.Wave.WaveProviders;
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
        private BufferedWaveProvider _bufferedWaveProvider;
        private Stream _reader;
        private volatile StreamingPlaybackState _playbackState;
        private readonly object _repositionLocker = new object();
        private volatile bool _fullyDownloaded;
        private const int MaxBufferSizeSeconds = 3;
        [AlsoNotifyFor(nameof(SpeedNormal), nameof(SpeedFast), nameof(SpeedFastest))]
        private Speed SpeedState { get; set; } = Speed.Normal;
        private bool IsBufferNearlyFull =>
            _bufferedWaveProvider != null &&
            _bufferedWaveProvider.BufferLength - _bufferedWaveProvider.BufferedBytes
            < _bufferedWaveProvider.WaveFormat.AverageBytesPerSecond / MaxBufferSizeSeconds;

        public bool IsBuffering => _playbackState == StreamingPlaybackState.Buffering;
        public long BufferProgress { get; set; }
        public bool SpeedNormal => SpeedState == Speed.Normal;
        public bool SpeedFast => SpeedState == Speed.Fast;
        public bool SpeedFastest => SpeedState == Speed.Fastest;
        public ICommand SpeedCommand { get; }

        /// <summary>
        /// The MP3 wave format (n.b. NOT the output format of this stream - see the WaveFormat property)
        /// </summary>
        public Mp3WaveFormat Mp3WaveFormat { get; private set; }

        public StreamingViewModel()
        {
            AppBaseTitle = "Streaming MP3 Player";
            SetTitle("File Not Loaded");
            SpeedCommand = new RelayCommand(OnChangedSpeed);
            IsStreaming = true;
            InputPath = "https://file-examples-com.github.io/uploads/2017/11/file_example_MP3_5MG.mp3";
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
        protected override void OnBackward()
        {
            if (_reader != null)
            {
                _reader.Position = Math.Max(_reader.Position - (long)Mp3WaveFormat.AverageBytesPerSecond * 10, 0);
                _bufferedWaveProvider.ClearBuffer();
                OnPropertyChanged(nameof(Position));
            }
        }
        protected override void OnForward()
        {
            if (_reader != null)
            {
                _reader.Position = Math.Min(_reader.Position + (long)Mp3WaveFormat.AverageBytesPerSecond * 10, _reader.Length-1);
                _bufferedWaveProvider.ClearBuffer();
                OnPropertyChanged(nameof(Position));
            }
        }
        protected override void UpdatePlayerState()
        {
            base.UpdatePlayerState();
            OnPropertyChanged(nameof(IsBuffering));
        }

        protected override void OpenFile()
        {
            try
            {
                var ofd = new OpenFileDialog { Filter = "MP3 files (*.mp3)|*.mp3" };
                if (ofd.ShowDialog() == true)
                {
                    Stop();
                    InputPath = ofd.FileName;
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
            if (_playbackState == StreamingPlaybackState.Playing || _playbackState == StreamingPlaybackState.Buffering)
            {
                WavePlayer?.Pause();
                _playbackState = StreamingPlaybackState.Paused;

                UpdatePlayerState();
                TaskbarOverlay = (ImageSource)Application.Current.FindResource("PauseImage");
                SetTitle("Pause " + Path.GetFileName(InputPath));
            }
        }
        protected override void Play()
        {
            if (string.IsNullOrEmpty(InputPath))
            {
                MessageBox.Show("Select a valid input file or URL first");
                return;
            }

            if (_playbackState == StreamingPlaybackState.Stopped)
            {
                _playbackState = StreamingPlaybackState.Buffering;
                _bufferedWaveProvider = null;

                // streaming play from HTTP protocol
                if (InputPath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    Task.Run(() => DownloadMp3(InputPath));
                }
                else // streaming play from File protocol
                {
                    Task.Run(() => OpenMp3File(InputPath));
                }

                PlayerTimer.Start();
            }
            else if (_playbackState == StreamingPlaybackState.Paused)
            {
                _playbackState = StreamingPlaybackState.Buffering;
            }

            UpdatePlayerState();
            TaskbarOverlay = (ImageSource)Application.Current.FindResource("PlayImage");
            SetTitle("Playing " + Path.GetFileName(InputPath));
        }
        protected override void Stop()
        {
            StopPlayback();
            TaskbarOverlay = null;
            Position = 0;
            SetTitle(Path.GetFileName(InputPath));
        }

        protected override void OnTick(object sender, EventArgs eventArgs)
        {
            try
            {
                if (_playbackState != StreamingPlaybackState.Stopped)
                {
                    if (_reader != null)
                    {
                        lock (_repositionLocker)
                        {
                            PositionChanging = true;
                            // var newPos = Math.Min(MaxPosition, _reader.Position - _bufferedWaveProvider?.BufferedBytes ?? 0);
                            var newPos = Math.Min(MaxPosition, _reader.Position);
                            Position = newPos < 0 ? _reader.Position : newPos;
                            OnPropertyChanged(nameof(PositionPercent));
                        }
                    }
                    if (WavePlayer == null && _bufferedWaveProvider != null)
                    {
                        Debug.WriteLine("Creating WaveOut Device");
                        CreatePlayer();
                        VolumeProvider = new VolumeWaveProvider16(_bufferedWaveProvider) { Volume = Volume / 100 };
                        WavePlayer.Init(VolumeProvider);
                    }
                    else if (_bufferedWaveProvider != null)
                    {
                        var bufferedSeconds = _bufferedWaveProvider.BufferedDuration.TotalSeconds;
                        ShowBufferState(bufferedSeconds);
                        // make it stutter less if we buffer up a decent amount before playing
                        if (bufferedSeconds < 0.5 && _playbackState == StreamingPlaybackState.Playing && !_fullyDownloaded)
                        {
                            _playbackState = StreamingPlaybackState.Buffering;
                            WavePlayer.Pause();
                        }
                        else if (bufferedSeconds >= 1 && _playbackState == StreamingPlaybackState.Buffering)
                        {
                            WavePlayer.Play();
                            _playbackState = StreamingPlaybackState.Playing;
                        }
                        else if (_fullyDownloaded && bufferedSeconds == 0)
                        {
                            Debug.WriteLine("Reached end of stream");
                            StopPlayback();
                        }
                    }
                    UpdatePlayerState();
                }
            }
            finally
            {
                PositionChanging = false;
            }
        }

        private void StopPlayback()
        {
            if (_playbackState != StreamingPlaybackState.Stopped)
            {
                if (!_fullyDownloaded)
                {
                    _reader?.Dispose();
                }

                _playbackState = StreamingPlaybackState.Stopped;
                if (WavePlayer != null)
                {
                    WavePlayer.Stop();
                    WavePlayer.Dispose();
                    WavePlayer = null;
                }
                PlayerTimer.Stop();
                // n.b. streaming thread may not yet have exited
                Thread.Sleep(500);
                ShowBufferState(0);
            }
        }

        protected override void OnPositionChanged()
        {
            if (_reader != null)
            {
                lock (_repositionLocker)
                {
                    if (PositionChanging == false) // position changed with timer so _reader is up to date
                    {
                        if (_reader is PartialHttpStream)
                        {
                            _playbackState = StreamingPlaybackState.Buffering;
                        }

                        _bufferedWaveProvider.ClearBuffer();
                        _reader.Position = Position;
                    }
                }
                CurrentTime = TimeSpan.FromSeconds((double)Position / Mp3WaveFormat.AverageBytesPerSecond);
                OnPropertyChanged(nameof(PositionPercent));
            }
        }

        private void OpenMp3File(string path)
        {
            var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read);
            _reader = new ReadFullyStream(fileStream, fileStream.Length);
            StreamMp3();
        }
        private void DownloadMp3(string url)
        {
            _reader = new PartialHttpStream(url);
            StreamMp3();
        }
        private void StreamMp3()
        {
            _fullyDownloaded = false;
            var buffer = new byte[16384 * MaxBufferSizeSeconds]; // needs to be big enough to hold a decompressed frame
            IMp3FrameDecompressor deCompressor = null;

            try
            {
                do
                {
                    if (IsBufferNearlyFull)
                    {
                        Debug.WriteLine("Buffer getting full, taking a break");
                        Thread.Sleep(500);
                    }
                    else
                    {
                        try
                        {
                            var frame = Mp3Frame.LoadFromStream(_reader);
                            if (frame != null)
                            {
                                if (deCompressor == null)
                                {
                                    // don't think these details matter too much - just help ACM select the right codec
                                    // however, the buffered provider doesn't know what sample rate it is working at
                                    // until we have a frame
                                    deCompressor = CreateFrameDeCompressor(frame);
                                    _bufferedWaveProvider = new BufferedWaveProvider(deCompressor.OutputFormat) {
                                        // allow us to get well ahead of ourselves
                                        BufferDuration = TimeSpan.FromSeconds(MaxBufferSizeSeconds),
                                        DiscardOnBufferOverflow = true,
                                        ReadFully = true
                                    };
                                    MaxPosition = _reader.Length;
                                    Duration = TimeSpan.FromSeconds((double)_reader.Length /
                                                                    Mp3WaveFormat.AverageBytesPerSecond);
                                }

                                int decompressed = deCompressor.DecompressFrame(frame, buffer, 0);
                                _bufferedWaveProvider.AddSamples(buffer, 0, decompressed);
                            }
                            else // end of stream
                            {
                                throw new EndOfStreamException("Stream fully loaded");
                            }
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
                _reader?.Dispose();
                deCompressor?.Dispose();
            }
        }
        private IMp3FrameDecompressor CreateFrameDeCompressor(Mp3Frame frame)
        {
            Mp3WaveFormat = new Mp3WaveFormat(frame.SampleRate, frame.ChannelMode == ChannelMode.Mono ? 1 : 2, frame.FrameLength, frame.BitRate);
            return new AcmMp3FrameDecompressor(Mp3WaveFormat);
        }
        private void ShowBufferState(double totalSeconds)
        {
            BufferProgress = (long)(MaxPosition * (CurrentTime.TotalSeconds + totalSeconds) / Duration.TotalSeconds);
        }

        public override void Dispose()
        {
            StopPlayback();
            base.Dispose();
        }
    }
}
