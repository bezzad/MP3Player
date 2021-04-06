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
        private HttpWebRequest _webRequest;
        private volatile StreamingPlaybackState _playbackState;
        private volatile bool _fullyDownloaded;
        [AlsoNotifyFor(nameof(SpeedNormal), nameof(SpeedFast), nameof(SpeedFastest))]
        private Speed SpeedState { get; set; } = Speed.Normal;
        private bool IsBufferNearlyFull =>
            _bufferedWaveProvider != null &&
            _bufferedWaveProvider.BufferLength - _bufferedWaveProvider.BufferedBytes
            < _bufferedWaveProvider.WaveFormat.AverageBytesPerSecond / 4;

        public bool IsBuffering => _playbackState == StreamingPlaybackState.Buffering;
        public int BufferProgress { get; set; }
        public int MaximumBufferProgress { get; set; }
        public string BufferProgressText { get; set; }
        public bool SpeedNormal => SpeedState == Speed.Normal;
        public bool SpeedFast => SpeedState == Speed.Fast;
        public bool SpeedFastest => SpeedState == Speed.Fastest;
        public ICommand SpeedCommand { get; }

        public StreamingViewModel()
        {
            AppBaseTitle = "Streaming MP3 Player";
            SetTitle("File Not Loaded");
            SpeedCommand = new RelayCommand(OnChangedSpeed);
            IsStreaming = true;
            InputPath = "http://stream.radiojavan.com/";
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

        }
        protected override void OnForward()
        {

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
                WavePlayer?.Pause();
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
                if (_playbackState == StreamingPlaybackState.Stopped)
                {
                    _playbackState = StreamingPlaybackState.Buffering;
                    _bufferedWaveProvider = null;
                    Task.Run(() => StreamMp3(InputPath));
                }
                else if (_playbackState == StreamingPlaybackState.Paused)
                {
                    _playbackState = StreamingPlaybackState.Buffering;
                }
            }

            PlayerTimer.Start();
            UpdatePlayerState();
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
            if (_playbackState != StreamingPlaybackState.Stopped)
            {
                if (WavePlayer == null && _bufferedWaveProvider != null)
                {
                    Debug.WriteLine("Creating WaveOut Device");
                    CreatePlayer();
                    VolumeProvider = new VolumeWaveProvider16(_bufferedWaveProvider) {Volume = Volume / 100};
                    WavePlayer.Init(VolumeProvider);
                    MaximumBufferProgress = (int) _bufferedWaveProvider.BufferDuration.TotalMilliseconds;
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
                    else if (bufferedSeconds > 4 && _playbackState == StreamingPlaybackState.Buffering)
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

        private void StopPlayback()
        {
            if (_playbackState != StreamingPlaybackState.Stopped)
            {
                if (!_fullyDownloaded)
                {
                    _webRequest.Abort();
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
            // if (_reader != null)
            // {
            //     _reader.Position = (long)(_reader.Length * Position / MaxPosition);
            //     CurrentTime = _reader.CurrentTime;
            //     OnPropertyChanged(nameof(PositionPercent));
            // }
        }

        private void StreamMp3(string url)
        {
            _fullyDownloaded = false;
            var buffer = new byte[16384 * 4]; // needs to be big enough to hold a decompressed frame
            IMp3FrameDecompressor deCompressor = null;

            try
            {
                _webRequest = (HttpWebRequest)WebRequest.Create(url);
                var resp = (HttpWebResponse)_webRequest.GetResponse();
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
        private void ShowBufferState(double totalSeconds)
        {
            BufferProgressText = $"{totalSeconds:0.0}s";
            BufferProgress = (int)(totalSeconds * 1000);
        }

        public override void Dispose()
        {
            StopPlayback();
            base.Dispose();
        }
    }
}
