using System;

namespace MP3Player.Wave
{
    /// <summary>
    /// Wave Format Padding Flags
    /// </summary>
    [Flags]
    public enum Mp3WaveFormatFlags
    {
        /// <summary>
        /// MPEGLAYER3_FLAG_PADDING_ISO
        /// </summary>
        PaddingIso = 0,
        /// <summary>
        /// MPEGLAYER3_FLAG_PADDING_ON
        /// </summary>
        PaddingOn = 1,
        /// <summary>
        /// MPEGLAYER3_FLAG_PADDING_OFF
        /// </summary>
        PaddingOff = 2,
    }
}
