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

namespace MP3Player.Sample
{
    public sealed class StreamingViewModel : PlayerViewModel
    {
        private BufferedWaveProvider _bufferedWaveProvider;
        private Stream _reader;
        private volatile StreamingPlaybackState _playbackState;
        private byte[] _decompressBuffer;
        private long _dataStartPosition;
        private readonly object _repositionLocker = new object();
        private volatile bool _fullyDownloaded;
        private const int MaxBufferSizeSeconds = 30;
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
            InputPath = "https://www.soundhelix.com/examples/mp3/SoundHelix-Song-3.mp3";
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
                lock (_repositionLocker)
                {
                    Position = Math.Max(Position - (long)Mp3WaveFormat.AverageBytesPerSecond * 10, 0);
                }
            }
        }
        protected override void OnForward()
        {
            if (_reader != null)
            {
                lock (_repositionLocker)
                {
                    Position = Math.Min(Position + (long)Mp3WaveFormat.AverageBytesPerSecond * 10, _reader.Length-1);
                }
            }
        }
        protected override void UpdatePlayerState()
        {
            base.UpdatePlayerState();
            OnPropertyChanged(nameof(IsBuffering));

            if (_playbackState == StreamingPlaybackState.Playing)
            {
                TaskbarOverlay = (ImageSource)Application.Current.FindResource("PlayImage");
            }
            else if (_playbackState == StreamingPlaybackState.Paused)
            {
                TaskbarOverlay= (ImageSource)Application.Current.FindResource("PauseImage");
            }
            else
            {
                TaskbarOverlay = null;
            }
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
            SetTitle("Playing " + Path.GetFileName(InputPath));
        }
        protected override void Stop()
        {
            if (_playbackState != StreamingPlaybackState.Stopped)
            {
                PlayerTimer.Stop(); // Note: stop timer before changing state
                Thread.Sleep(500);
                _playbackState = StreamingPlaybackState.Stopped;
                _reader?.Dispose();

                if (WavePlayer != null)
                {
                    WavePlayer.Stop();
                    WavePlayer.Dispose();
                    WavePlayer = null;
                }
                // n.b. streaming thread may not yet have exited
                Thread.Sleep(500);
                ShowBufferState(0);
            }
            UpdatePlayerState();
            Position = 0;
            SetTitle(Path.GetFileName(InputPath));
        }

        protected override void OnTick(object sender, EventArgs eventArgs)
        {
            try
            {
                if (_playbackState != StreamingPlaybackState.Stopped)
                {
                    UpdatePosition();

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
                            Stop();
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
        private void UpdatePosition()
        {
            if (_reader != null)
            {
                lock (_repositionLocker)
                {
                    PositionChanging = true;
                    var outputBufferedDuration = _bufferedWaveProvider?.BufferedDuration ?? new TimeSpan(0);
                    var inputBufferedBytes = outputBufferedDuration.TotalSeconds * (Mp3WaveFormat?.AverageBytesPerSecond ?? 0);
                    var newPos = Math.Min(MaxPosition, _reader.Position - (long)inputBufferedBytes - _dataStartPosition);
                    Position = newPos < 0 ? _reader.Position : newPos;
                    OnPropertyChanged(nameof(PositionPercent));
                }
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
                        // go to the begin of nearest frame
                        _reader.Position = Position + _dataStartPosition - (Position % Mp3WaveFormat.blockSize);
                        _fullyDownloaded = false;
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
                            lock (_repositionLocker)
                            {
                                if (deCompressor == null)
                                {
                                    // don't think these details matter too much - just help ACM select the right codec
                                    // however, the buffered provider doesn't know what sample rate it is working at
                                    // until we have a frame
                                    deCompressor = CreateFrameDeCompressor();
                                    _bufferedWaveProvider = new BufferedWaveProvider(deCompressor.OutputFormat) {
                                        // allow us to get well ahead of ourselves
                                        BufferDuration = TimeSpan.FromSeconds(MaxBufferSizeSeconds),
                                        DiscardOnBufferOverflow = true,
                                        ReadFully = true
                                    };
                                }
                                else
                                {
                                    var frame = Mp3Frame.LoadFromStream(_reader);
                                    if (frame != null)
                                    {
                                        int decompressed = deCompressor.DecompressFrame(frame, _decompressBuffer, 0);
                                        _bufferedWaveProvider.AddSamples(_decompressBuffer, 0, decompressed);
                                    }
                                    else // end of stream
                                    {
                                        _fullyDownloaded = true;
                                        if (_bufferedWaveProvider.BufferedBytes == 0)
                                        {
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        catch (EndOfStreamException)
                        {
                            _fullyDownloaded = true;
                            // reached the end of the MP3 file / stream
                            // break;
                        }
                    }

                } while (_playbackState != StreamingPlaybackState.Stopped);

                Debug.WriteLine("Exiting");
                // was doing this in a finally block, but for some reason
                // we are hanging on response stream .Dispose so never get there
                deCompressor?.Dispose();
            }
            catch (WebException e) 
                when (e.Status == WebExceptionStatus.RequestCanceled)
            {
                // ignored cancel exceptions
                Stop();
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
                Stop();
            }
            finally
            {
                _fullyDownloaded = true;
                deCompressor?.Dispose();
            }
        }
        private IMp3FrameDecompressor CreateFrameDeCompressor()
        {
            if (_reader == null)
                throw new ArgumentNullException(nameof(_reader));

            Id3v2Tag.ReadTag(_reader); // read tag data in from begin of stream
            
            _dataStartPosition = _reader.Position;
            var firstFrame = Mp3Frame.LoadFromStream(_reader);
            if (firstFrame == null)
                throw new InvalidDataException("Invalid MP3 file - no MP3 Frames Detected");
            double bitRate = firstFrame.BitRate;
            var xingHeader = XingHeader.LoadXingHeader(firstFrame);
            // If the header exists, we can skip over it when decoding the rest of the file
            if (xingHeader != null)
                _dataStartPosition = _reader.Position;

            // workaround for a longstanding issue with some files failing to load
            // because they report a spurious sample rate change
            var secondFrame = Mp3Frame.LoadFromStream(_reader);
            if (secondFrame != null &&
                (secondFrame.SampleRate != firstFrame.SampleRate ||
                 secondFrame.ChannelMode != firstFrame.ChannelMode))
            {
                // assume that the first frame was some kind of VBR/LAME header that we failed to recognise properly
                _dataStartPosition = secondFrame.FileOffset;
                // forget about the first frame, the second one is the first one we really care about
                firstFrame = secondFrame;
            }

            // create a temporary MP3 format before we know the real bitrate
            Mp3WaveFormat = new Mp3WaveFormat(firstFrame.SampleRate,
                firstFrame.ChannelMode == ChannelMode.Mono ? 1 : 2,
                firstFrame.FrameLength, (int)bitRate);

            var decompressor = new AcmMp3FrameDecompressor(Mp3WaveFormat);
            _decompressBuffer = new byte[16384 * 4]; // needs to be big enough to hold a decompressed frame
            MaxPosition = _reader.Length - _dataStartPosition;
            Duration = TimeSpan.FromSeconds((double)MaxPosition / Mp3WaveFormat.AverageBytesPerSecond);

            return decompressor;
        }
        private void ShowBufferState(double totalSeconds)
        {
            BufferProgress = (long)(MaxPosition * (CurrentTime.TotalSeconds + totalSeconds) / Duration.TotalSeconds);
        }

        public override void Dispose()
        {
            Stop();
            base.Dispose();
        }
    }
}
