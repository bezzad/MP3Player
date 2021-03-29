using System.Runtime.InteropServices;

namespace MP3Player
{
    /// <summary>
    /// MP3 WaveFormat, MPEGLAYER3WAVEFORMAT from mmreg.h
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 2)]
    public class Mp3WaveFormat : WaveFormat
    {
        /// <summary>
        /// Wave format ID (wID)
        /// </summary>
        public Mp3WaveFormatId id;
        /// <summary>
        /// Padding flags (fdwFlags)
        /// </summary>
        public Mp3WaveFormatFlags flags;
        /// <summary>
        /// Block Size (nBlockSize)
        /// </summary>
        public ushort blockSize;
        /// <summary>
        /// Frames per block (nFramesPerBlock)
        /// </summary>
        public ushort framesPerBlock;
        /// <summary>
        /// Codec Delay (nCodecDelay)
        /// </summary>
        public ushort codecDelay;

        private const short Mp3WaveFormatExtraBytes = 12; // MPEGLAYER3_WFX_EXTRA_BYTES

        /// <summary>
        /// Creates a new MP3 WaveFormat
        /// </summary>
        public Mp3WaveFormat(int sampleRate, int channels, int blockSize, int bitRate)
        {
            waveFormatTag = WaveFormatEncoding.MpegLayer3;
            this.channels = (short)channels;
            this.averageBytesPerSecond = bitRate / 8;
            this.bitsPerSample = 0; // must be zero
            this.blockAlign = 1; // must be 1
            this.sampleRate = sampleRate;

            this.extraSize = Mp3WaveFormatExtraBytes;
            this.id = Mp3WaveFormatId.Mpeg;
            this.flags = Mp3WaveFormatFlags.PaddingIso;
            this.blockSize = (ushort)blockSize;
            this.framesPerBlock = 1;
            this.codecDelay = 0;
        }
    }
}
