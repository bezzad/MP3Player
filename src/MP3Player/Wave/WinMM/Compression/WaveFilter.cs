using System.Runtime.InteropServices;

namespace MP3Player.Wave.WinMM.Compression
{
    /// <summary>
    /// Summary description for WaveFilter.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public class WaveFilter
    {
        /// <summary>
        /// cbStruct
        /// </summary>
        public int StructureSize = Marshal.SizeOf(typeof(WaveFilter));
        /// <summary>
        /// dwFilterTag
        /// </summary>
        public int FilterTag = 0;
        /// <summary>
        /// fdwFilter
        /// </summary>
        public int Filter = 0;
        /// <summary>
        /// reserved
        /// </summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        public int[] Reserved = null;
    }
}
