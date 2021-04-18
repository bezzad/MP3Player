using System;
using System.Text;

namespace MP3Player.Utils
{
    /// <summary>
    /// these will become extension methods once we move to .NET 3.5
    /// </summary>
    public static class ByteArrayExtensions
    {
        /// <summary>
        /// Checks if the buffer passed in is entirely full of nulls
        /// </summary>
        public static bool IsEntirelyNull(byte[] buffer)
        {
            foreach (byte b in buffer)
                if (b != 0)
                    return false;
            return true;
        }

        /// <summary>
        /// Converts to a string containing the buffer described in hex
        /// </summary>
        public static string DescribeAsHex(byte[] buffer, string separator, int bytesPerLine)
        {
            StringBuilder sb = new StringBuilder();
            int n = 0;
            foreach (byte b in buffer)
            {
                sb.AppendFormat("{0:X2}{1}", b, separator);
                if (++n % bytesPerLine == 0)
                    sb.Append("\r\n");
            }
            sb.Append("\r\n");
            return sb.ToString();
        }

        /// <summary>
        /// Decodes the buffer using the specified encoding, stopping at the first null
        /// </summary>
        public static string DecodeAsString(byte[] buffer, int offset, int length, Encoding encoding)
        {
            for (int n = 0; n < length; n++)
            {
                if (buffer[offset + n] == 0)
                    length = n;
            }
            return encoding.GetString(buffer, offset, length);
        }

        /// <summary>
        /// Concatenates the given arrays into a single array.
        /// </summary>
        /// <param name="byteArrays">The arrays to concatenate</param>
        /// <returns>The concatenated resulting array.</returns>
        public static byte[] Concat(params byte[][] byteArrays)
        {
            int size = 0;
            foreach (byte[] btArray in byteArrays)
            {
                size += btArray.Length;
            }

            if (size <= 0)
            {
                return new byte[0];
            }

            byte[] result = new byte[size];
            int idx = 0;
            foreach (byte[] btArray in byteArrays)
            {
                Array.Copy(btArray, 0, result, idx, btArray.Length);
                idx += btArray.Length;
            }

            return result;
        }

        /// <summary>
        ///     Copies a range of elements from an System.Array starting at the specified source
        ///     index and pastes them to another System.Array starting at the specified destination
        ///     index. The length and the indexes are specified as 64-bit integers.
        /// </summary>
        /// <param name="sourceArray">The System.Array that contains the data to copy.</param>
        /// <param name="sourceIndex">A 64-bit integer that represents the index in the sourceArray at which copying begins.</param>
        /// <param name="destinationArray">The System.Array that receives the data.</param>
        /// <param name="destinationIndex">A 64-bit integer that represents the index in the destinationArray at which storing begins.</param>
        /// <param name="length">
        ///     A 64-bit integer that represents the number of elements to copy. The integer
        ///     must be between zero and System.Int32.MaxValue, inclusive.
        /// </param>
        /// <exception cref="System.ArgumentNullException">sourceArray is null. -or- destinationArray is null.</exception>
        /// <exception cref="System.RankException">sourceArray and destinationArray have different ranks.</exception>
        /// <exception cref="System.ArrayTypeMismatchException">sourceArray and destinationArray are of incompatible types.</exception>
        /// <exception cref="System.InvalidCastException">At least one element in sourceArray cannot be cast to the type of destinationArray.</exception>
        /// <exception cref="System.ArgumentOutOfRangeException">
        ///     sourceIndex is outside the range of valid indexes for the sourceArray. -or- destinationIndex
        ///     is outside the range of valid indexes for the destinationArray. -or- length is
        ///     less than 0 or greater than System.Int32.MaxValue.
        /// </exception>
        /// <exception cref="System.ArgumentException">
        ///     length is greater than the number of elements from sourceIndex to the end of
        ///     sourceArray. -or- length is greater than the number of elements from destinationIndex
        ///     to the end of destinationArray.
        /// </exception>
        public static void Copy(this byte[] sourceArray, long sourceIndex, byte?[] destinationArray, long destinationIndex, long length)
        {
            long offset = 0;
            while (offset < length)
            {
                destinationArray[destinationIndex + offset] = sourceArray[sourceIndex + offset];
                offset++;
            }
        }
    }
}
