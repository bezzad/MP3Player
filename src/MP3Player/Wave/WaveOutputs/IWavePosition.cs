﻿using MP3Player.Wave.WaveFormats;

namespace MP3Player.Wave.WaveOutputs
{
    /// <summary>
    /// Interface for IWavePlayers that can report position
    /// </summary>
    public interface IWavePosition
    {
        /// <summary>
        /// Position (in terms of bytes played - does not necessarily translate directly to the position within the source audio file)
        /// </summary>
        /// <returns>Position in bytes</returns>
        long GetPosition();

        /// <summary>
        /// Gets a <see cref="WaveFormat"/> instance indicating the format the hardware is using.
        /// </summary>
        WaveFormat OutputWaveFormat { get; }
    }
}
