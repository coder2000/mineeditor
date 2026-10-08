namespace MineEditor.Core.Map;

/// <summary>
/// Reads fixed-width values packed into a <c>long[]</c>, as used for block states and heightmaps.
/// </summary>
/// <remarks>
/// Since 1.16 entries never straddle two longs ("padded"); 1.13–1.15 packed them end to end ("spanning").
/// </remarks>
internal readonly struct PackedLongArray
{
    private readonly long[] _data;
    private readonly int _bits;
    private readonly int _perLong;
    private readonly ulong _mask;
    private readonly bool _spanning;

    private PackedLongArray(long[] data, int bits, bool spanning)
    {
        _data = data;
        _bits = bits;
        _perLong = 64 / bits;
        _mask = (1UL << bits) - 1;
        _spanning = spanning;
    }

    /// <summary>
    /// Creates a reader for <paramref name="count"/> entries of at least <paramref name="minBits"/> bits,
    /// working out the bit width and layout from the array length.
    /// </summary>
    public static bool TryCreate(long[] data, int count, int minBits, out PackedLongArray array)
    {
        for (var bits = minBits; bits <= 32; bits++)
        {
            var perLong = 64 / bits;
            if (data.Length == (count + perLong - 1) / perLong)
            {
                array = new PackedLongArray(data, bits, spanning: false);
                return true;
            }

            if ((long)data.Length * 64 == (long)count * bits)
            {
                array = new PackedLongArray(data, bits, spanning: true);
                return true;
            }
        }

        array = default;
        return false;
    }

    /// <summary>Gets the number of bits needed to index a palette of <paramref name="size"/> entries.</summary>
    public static int BitsFor(int size) => size <= 1 ? 0 : 32 - int.LeadingZeroCount(size - 1);

    public int this[int index]
    {
        get
        {
            if (!_spanning)
            {
                var value = (ulong)_data[index / _perLong] >> (index % _perLong * _bits);
                return (int)(value & _mask);
            }

            var bitIndex = index * _bits;
            var longIndex = bitIndex >> 6;
            var offset = bitIndex & 63;
            var result = (ulong)_data[longIndex] >> offset;
            if (offset + _bits > 64)
            {
                result |= (ulong)_data[longIndex + 1] << (64 - offset);
            }

            return (int)(result & _mask);
        }
    }
}
