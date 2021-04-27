using MP3Player.Wave.FileFormats.MP3;
using MP3Player.Wave.WaveFormats;
using MP3Player.Wave.WaveStreams;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using MP3Player.Wave.WinMM;

namespace MP3Player
{
    /// <summary>
    /// Class for reading from MP3 streams
    /// </summary>
    public class Mp3StreamReader : WaveStream
    {
        private Stream _mp3Stream;
        private readonly long _mp3DataLength;
        private readonly long _dataStartPosition; // the length of wasted bytes which is headers or Id3 tags
        private readonly int _bytesPerSample;
        private readonly int _bytesPerDecodedFrame;
        private IMp3FrameDecompressor _decompressor;
        private readonly byte[] _decompressBuffer;
        private int _decompressBufferOffset;
        private int _decompressLeftovers;
        private bool _repositionedFlag;
        private readonly object _repositionLock = new object();

        /// <summary>
        /// The MP3 wave format (n.b. NOT the output format of this stream - see the WaveFormat property)
        /// </summary>
        public Mp3WaveFormat Mp3WaveFormat { get; private set; }

        /// <summary>
        /// ID3v2 tag if present
        /// </summary>
        // ReSharper disable once InconsistentNaming
        public Id3v2Tag Id3v2Tag { get; }

        /// <summary>
        /// ID3v1 tag if present
        /// </summary>
        // ReSharper disable once InconsistentNaming
        public byte[] Id3v1Tag { get; }

        /// <summary>
        /// This is the length in bytes of data available to be read out from the Read method
        /// (i.e. the decompressed MP3 length)
        /// n.b. this may return 0 for files whose length is unknown
        /// </summary>
        public override long Length => _mp3Stream.Length - _dataStartPosition;

        /// <summary>
        /// Xing header if present
        /// </summary>
        public XingHeader XingHeader { get; }

        /// <summary>
        /// <see cref="WaveStream.WaveFormat"/>
        /// </summary>
        public override WaveFormat WaveFormat { get; }

        /// <summary>
        /// <see cref="Stream.Position"/>
        /// </summary>
        public override long Position
        {
            get => _mp3Stream.Position - _dataStartPosition;
            set
            {
                lock (_repositionLock)
                {
                    value = Math.Max(Math.Min(value, Length), 0);
                   
                    _decompressBufferOffset = 0;
                    _decompressLeftovers = 0;
                    _repositionedFlag = true;
                    _mp3Stream.Position = value + _dataStartPosition;
                }
            }
        }

        /// <summary>
        /// Opens MP3 from a stream rather than a file
        /// Will not dispose of this stream itself
        /// </summary>
        /// <param name="inputStream">The incoming stream containing MP3 data</param>
        public Mp3StreamReader(Stream inputStream)
        {
            if (inputStream == null)
                throw new ArgumentNullException(nameof(inputStream));
            try
            {
                _mp3Stream = inputStream;
                Id3v2Tag = Id3v2Tag.ReadTag(_mp3Stream);

                _dataStartPosition = _mp3Stream.Position;
                var firstFrame = Mp3Frame.LoadFromStream(_mp3Stream);
                if (firstFrame == null)
                    throw new InvalidDataException("Invalid MP3 file - no MP3 Frames Detected");
                double bitRate = firstFrame.BitRate;
                XingHeader = XingHeader.LoadXingHeader(firstFrame);
                // If the header exists, we can skip over it when decoding the rest of the file
                if (XingHeader != null)
                    _dataStartPosition = _mp3Stream.Position;

                // workaround for a longstanding issue with some files failing to load
                // because they report a spurious sample rate change
                var secondFrame = Mp3Frame.LoadFromStream(_mp3Stream);
                if (secondFrame != null &&
                    (secondFrame.SampleRate != firstFrame.SampleRate ||
                     secondFrame.ChannelMode != firstFrame.ChannelMode))
                {
                    // assume that the first frame was some kind of VBR/LAME header that we failed to recognise properly
                    _dataStartPosition = secondFrame.FileOffset;
                    // forget about the first frame, the second one is the first one we really care about
                    firstFrame = secondFrame;
                }

                _mp3DataLength = _mp3Stream.Length - _dataStartPosition;

                // try for an ID3v1 tag as well
                _mp3Stream.Position = _mp3Stream.Length - 128;
                byte[] tag = new byte[128];
                _mp3Stream.Read(tag, 0, 128);
                if (tag[0] == 'T' && tag[1] == 'A' && tag[2] == 'G')
                {
                    Id3v1Tag = tag;
                    _mp3DataLength -= 128;
                }

                _mp3Stream.Position = _dataStartPosition;

                // create a temporary MP3 format before we know the real bitrate
                Mp3WaveFormat = new Mp3WaveFormat(firstFrame.SampleRate,
                    firstFrame.ChannelMode == ChannelMode.Mono ? 1 : 2,
                    firstFrame.FrameLength, (int)bitRate);

                _decompressor = CreateAcmFrameDecompressor(Mp3WaveFormat);
                WaveFormat = _decompressor.OutputFormat;
                _bytesPerSample = (_decompressor.OutputFormat.BitsPerSample) / 8 * _decompressor.OutputFormat.Channels;

                // no MP3 frames have more than 1152 samples in them
                _bytesPerDecodedFrame = 1152 * _bytesPerSample;

                // some MP3s I seem to get double
                _decompressBuffer = new byte[_bytesPerDecodedFrame * 2];
            }
            catch (Exception)
            {
                inputStream.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Reads the next mp3 frame
        /// </summary>
        /// <returns>Next mp3 frame, or null if EOF</returns>
        public Mp3Frame ReadNextFrame()
        {
            var frame = ReadNextFrame(true);
            return frame;
        }

        /// <summary>
        /// Reads the next mp3 frame
        /// </summary>
        /// <returns>Next mp3 frame, or null if EOF</returns>
        private Mp3Frame ReadNextFrame(bool readData)
        {
            Mp3Frame frame = null;
            try
            {
                frame = Mp3Frame.LoadFromStream(_mp3Stream, readData);
            }
            catch (EndOfStreamException)
            {
                // suppress for now - it means we unexpectedly got to the end of the stream
                // half way through
            }
            return frame;
        }

        /// <summary>
        /// Reads decompressed PCM data from our MP3 file.
        /// </summary>
        public override int Read(byte[] sampleBuffer, int offset, int numBytes)
        {
            int bytesRead = 0;
            lock (_repositionLock)
            {
                if (_decompressLeftovers != 0)
                {
                    int toCopy = Math.Min(_decompressLeftovers, numBytes);
                    Array.Copy(_decompressBuffer, _decompressBufferOffset, sampleBuffer, offset, toCopy);
                    _decompressLeftovers -= toCopy;
                    if (_decompressLeftovers == 0)
                    {
                        _decompressBufferOffset = 0;
                    }
                    else
                    {
                        _decompressBufferOffset += toCopy;
                    }
                    bytesRead += toCopy;
                    offset += toCopy;
                }
            }
            Debug.Assert(bytesRead <= numBytes, "MP3 File Reader read too much");
            return bytesRead;
        }

        /// <summary>
        /// Creates an ACM MP3 Frame decompressor. This is the default with NAudio
        /// </summary>
        /// <param name="mp3Format">A WaveFormat object based </param>
        /// <returns></returns>
        public static IMp3FrameDecompressor CreateAcmFrameDecompressor(WaveFormat mp3Format)
        {
            // new DmoMp3FrameDecompressor(this.Mp3WaveFormat); 
            return new AcmMp3FrameDecompressor(mp3Format);
        }

        /// <summary>
        /// Disposes this WaveStream
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_mp3Stream != null)
                {
                    _mp3Stream.Dispose();
                    _mp3Stream = null;
                }
                if (_decompressor != null)
                {
                    _decompressor.Dispose();
                    _decompressor = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
