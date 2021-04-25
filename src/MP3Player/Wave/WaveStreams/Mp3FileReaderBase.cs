using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using MP3Player.Wave.FileFormats.MP3;
using MP3Player.Wave.WaveFormats;

namespace MP3Player.Wave.WaveStreams
{
    /// <summary>
    /// Class for reading from MP3 files
    /// </summary>
    public class Mp3FileReaderBase : WaveStream
    {
        private Stream _mp3Stream;
        private readonly long _mp3DataLength;
        private readonly long _dataStartPosition;
        private readonly bool _ownInputStream;
        private List<Mp3Index> _tableOfContents;
        private int _tocIndex;
        private long _totalSamples;
        private readonly int _bytesPerSample;
        private readonly int _bytesPerDecodedFrame;
        private IMp3FrameDecompressor _decompressor;
        private readonly byte[] _decompressBuffer;
        private int _decompressBufferOffset;
        private int _decompressLeftovers;
        private bool _repositionedFlag;
        private long _position; // decompressed data position tracker
        private readonly object _repositionLock = new object();

        /// <summary>
        /// The MP3 wave format (n.b. NOT the output format of this stream - see the WaveFormat property)
        /// </summary>
        public Mp3WaveFormat Mp3WaveFormat { get; private set; }

        /// <summary>Supports opening a MP3 file</summary>
        /// <param name="mp3FileName">MP3 File name</param>
        /// <param name="frameDecompressorBuilder">Factory method to build a frame decompressor</param>
        public Mp3FileReaderBase(string mp3FileName, FrameDecompressorBuilder frameDecompressorBuilder)
            : this(File.OpenRead(mp3FileName), frameDecompressorBuilder, true)
        {
        }

        /// <summary>
        /// Opens MP3 from a stream rather than a file
        /// Will not dispose of this stream itself
        /// </summary>
        /// <param name="inputStream">The incoming stream containing MP3 data</param>
        /// <param name="frameDecompressorBuilder">Factory method to build a frame decompressor</param>
        public Mp3FileReaderBase(Stream inputStream, FrameDecompressorBuilder frameDecompressorBuilder)
            : this(inputStream, frameDecompressorBuilder, false)
        {

        }

        protected Mp3FileReaderBase(Stream inputStream, FrameDecompressorBuilder frameDecompressorBuilder, bool ownInputStream)
        {
            if (inputStream == null)
                throw new ArgumentNullException(nameof(inputStream));
            if (frameDecompressorBuilder == null)
                throw new ArgumentNullException(nameof(frameDecompressorBuilder));
            this._ownInputStream = ownInputStream;
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

                CreateTableOfContents();
                _tocIndex = 0;

                // [Bit rate in Kilobits/sec] = [Length in kbits] / [time in seconds] 
                //                            = [Length in bits ] / [time in milliseconds]

                // Note: in audio, 1 kilobit = 1000 bits.
                // Calculated as a double to minimize rounding errors
                bitRate = (_mp3DataLength*8.0/TotalSeconds());

                _mp3Stream.Position = _dataStartPosition;

                // now we know the real bitrate we can create an accurate MP3 WaveFormat
                Mp3WaveFormat = new Mp3WaveFormat(firstFrame.SampleRate,
                    firstFrame.ChannelMode == ChannelMode.Mono ? 1 : 2, firstFrame.FrameLength, (int)bitRate);
                _decompressor = frameDecompressorBuilder(Mp3WaveFormat);
                WaveFormat = _decompressor.OutputFormat;
                _bytesPerSample = (_decompressor.OutputFormat.BitsPerSample)/8*_decompressor.OutputFormat.Channels;
                // no MP3 frames have more than 1152 samples in them
                _bytesPerDecodedFrame = 1152 * _bytesPerSample;
                // some MP3s I seem to get double
                _decompressBuffer = new byte[_bytesPerDecodedFrame * 2];
            }
            catch (Exception)
            {
                if (ownInputStream)
                    inputStream.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Function that can create an MP3 Frame decompressor
        /// </summary>
        /// <param name="mp3Format">A WaveFormat object describing the MP3 file format</param>
        /// <returns>An MP3 Frame decompressor</returns>
        public delegate IMp3FrameDecompressor FrameDecompressorBuilder(WaveFormat mp3Format);

        private void CreateTableOfContents()
        {
            try
            {
                // Just a guess at how many entries we'll need so the internal array need not resize very much
                // 400 bytes per frame is probably a good enough approximation.
                _tableOfContents = new List<Mp3Index>((int)(_mp3DataLength / 400));
                Mp3Frame frame;
                do
                {
                    var index = new Mp3Index
                    {
                        FilePosition = _mp3Stream.Position,
                        SamplePosition = _totalSamples
                    };
                    frame = ReadNextFrame(false);
                    if (frame != null)
                    {
                        ValidateFrameFormat(frame);

                        _totalSamples += frame.SampleCount;
                        index.SampleCount = frame.SampleCount;
                        index.ByteCount = (int)(_mp3Stream.Position - index.FilePosition);
                        _tableOfContents.Add(index);
                    }
                } while (frame != null);
            }
            catch (EndOfStreamException)
            {
                // not necessarily a problem
            }
        }

        private void ValidateFrameFormat(Mp3Frame frame)
        {
            if (frame.SampleRate != Mp3WaveFormat.SampleRate)
            {
                string message =
                    $"Got a frame at sample rate {frame.SampleRate}, in an MP3 with sample rate {Mp3WaveFormat.SampleRate}. Mp3FileReader does not support sample rate changes.";
                throw new InvalidOperationException(message);
            }
            int channels = frame.ChannelMode == ChannelMode.Mono ? 1 : 2;
            if (channels != Mp3WaveFormat.Channels)
            {
                string message =
                    $"Got a frame with channel mode {frame.ChannelMode}, in an MP3 with {Mp3WaveFormat.Channels} channels. Mp3FileReader does not support changes to channel count.";
                throw new InvalidOperationException(message);
            }
        }

        /// <summary>
        /// Gets the total length of this file in milliseconds.
        /// </summary>
        private double TotalSeconds()
        {
            return (double)_totalSamples / Mp3WaveFormat.SampleRate;
        }

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
        /// Reads the next mp3 frame
        /// </summary>
        /// <returns>Next mp3 frame, or null if EOF</returns>
        public Mp3Frame ReadNextFrame()
        {
            var frame = ReadNextFrame(true);
            if (frame != null)
                _position += frame.SampleCount*_bytesPerSample;
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
                if (frame != null)
                {
                    _tocIndex++;
                }
            }
            catch (EndOfStreamException)
            {
                // suppress for now - it means we unexpectedly got to the end of the stream
                // half way through
            }
            return frame;
        }

        /// <summary>
        /// This is the length in bytes of data available to be read out from the Read method
        /// (i.e. the decompressed MP3 length)
        /// n.b. this may return 0 for files whose length is unknown
        /// </summary>
        public override long Length => _totalSamples * _bytesPerSample;

        /// <summary>
        /// <see cref="WaveStream.WaveFormat"/>
        /// </summary>
        public override WaveFormat WaveFormat { get; }

        /// <summary>
        /// <see cref="Stream.Position"/>
        /// </summary>
        public override long Position
        {
            get => _position;
            set
            {
                lock (_repositionLock)
                {
                    value = Math.Max(Math.Min(value, Length), 0);
                    var samplePosition = value / _bytesPerSample;
                    Mp3Index mp3Index = null;
                    for (int index = 0; index < _tableOfContents.Count; index++)
                    {
                        if (_tableOfContents[index].SamplePosition + _tableOfContents[index].SampleCount > samplePosition)
                        {
                            mp3Index = _tableOfContents[index];
                            _tocIndex = index;
                            break;
                        }
                    }

                    _decompressBufferOffset = 0;
                    _decompressLeftovers = 0;
                    _repositionedFlag = true;

                    if (mp3Index != null)
                    {
                        // perform the reposition
                        _mp3Stream.Position = mp3Index.FilePosition;

                        // set the offset into the buffer (that is yet to be populated in Read())
                        var frameOffset = samplePosition - mp3Index.SamplePosition;
                        if (frameOffset > 0)
                        {
                            _decompressBufferOffset = (int)frameOffset * _bytesPerSample;
                        }
                    }
                    else
                    {
                        // we are repositioning to the end of the data
                        _mp3Stream.Position = _mp3DataLength + _dataStartPosition;
                    }

                    _position = value;
                }
            }
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

                int targetTocIndex = _tocIndex; // the frame index that contains the requested data

                if (_repositionedFlag)
                {
                    _decompressor.Reset();

                    // Seek back a few frames of the stream to get the reset decoder decode a few
                    // warm-up frames before reading the requested data. Without the warm-up phase,
                    // the first half of the frame after the reset is attenuated and does not resemble
                    // the data as it would be when reading sequentially from the beginning, because 
                    // the decoder is missing the required overlap from the previous frame.
                    _tocIndex = Math.Max(0, _tocIndex - 3); // no warm-up at the beginning of the stream
                    _mp3Stream.Position = _tableOfContents[_tocIndex].FilePosition;

                    _repositionedFlag = false;
                }

                while (bytesRead < numBytes)
                {
                    Mp3Frame frame = ReadNextFrame(true); // internal read - should not advance position
                    if (frame != null)
                    {
                        int decompressed = _decompressor.DecompressFrame(frame, _decompressBuffer, 0);

                        if (_tocIndex <= targetTocIndex || decompressed == 0)
                        {
                            // The first frame after a reset usually does not immediately yield decoded samples.
                            // Because the next instructions will fail if a buffer offset is set and the frame 
                            // decoding didn't return data, we skip the part.
                            // We skip the following instructions also after decoding a warm-up frame.
                            continue;
                        }
                        // Two special cases can happen here:
                        // 1. We are interested in the first frame of the stream, but need to read the second frame too
                        //    for the decoder to return decoded data
                        // 2. We are interested in the second frame of the stream, but because reading the first frame
                        //    as warm-up didn't yield any data (because the decoder needs two frames to return data), we
                        //    get data from the first and second frame. 
                        //    This case needs special handling, and we have to purge the data of the first frame.
                        else if (_tocIndex == targetTocIndex + 1 && decompressed == _bytesPerDecodedFrame * 2)
                        {
                            // Purge the first frame's data
                            Array.Copy(_decompressBuffer, _bytesPerDecodedFrame, _decompressBuffer, 0, _bytesPerDecodedFrame);
                            decompressed = _bytesPerDecodedFrame;
                        }

                        int toCopy = Math.Min(decompressed - _decompressBufferOffset, numBytes - bytesRead);
                        Array.Copy(_decompressBuffer, _decompressBufferOffset, sampleBuffer, offset, toCopy);
                        if ((toCopy + _decompressBufferOffset) < decompressed)
                        {
                            _decompressBufferOffset = toCopy + _decompressBufferOffset;
                            _decompressLeftovers = decompressed - _decompressBufferOffset;
                        }
                        else
                        {
                            // no leftovers
                            _decompressBufferOffset = 0;
                            _decompressLeftovers = 0;
                        }
                        offset += toCopy;
                        bytesRead += toCopy;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            Debug.Assert(bytesRead <= numBytes, "MP3 File Reader read too much");
            _position += bytesRead;
            return bytesRead;
        }

        /// <summary>
        /// Xing header if present
        /// </summary>
        public XingHeader XingHeader { get; }

        /// <summary>
        /// Disposes this WaveStream
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_mp3Stream != null)
                {
                    if (_ownInputStream)
                    {
                        _mp3Stream.Dispose();
                    }
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
