// LZMA decoder used to open the game's configBinary (DarkConfig, compression 3 = LZMA "alone" format).
//
// Port of the public-domain LZMA SDK decoder (the same code the client ships in
// Assembly-CSharp-firstpass: SevenZip.Compression.LZMA.Decoder), simplified to decode into one byte array:
// the output array itself is the dictionary, so there is no sliding window.
//
// The logic was checked byte-for-byte against Python's lzma module on a configBinary from build 190828.
public static class LzmaDecoder
{
    private const int NumStates = 12;
    private const uint TopValue = 1u << 24;

    // Decodes "props (5 bytes) + raw stream" starting at data[offset]. outSize is the uncompressed size.
    public static byte[] Decode(byte[] props, byte[] data, int offset, long outSize)
    {
        if (props.Length < 5) throw new InvalidDataException("LZMA: properties too short");
        if (outSize < 0 || outSize > int.MaxValue) throw new InvalidDataException("LZMA: bad output size " + outSize);

        int lc = props[0] % 9;
        int rest = props[0] / 9;
        int lp = rest % 5;
        int pb = rest / 5;
        if (pb > 4) throw new InvalidDataException("LZMA: bad pb");

        var output = new byte[outSize];
        var rc = new RangeDecoder(data, offset);

        ushort[] isMatch = NewProbs(NumStates << 4);
        ushort[] isRep = NewProbs(NumStates);
        ushort[] isRepG0 = NewProbs(NumStates);
        ushort[] isRepG1 = NewProbs(NumStates);
        ushort[] isRepG2 = NewProbs(NumStates);
        ushort[] isRep0Long = NewProbs(NumStates << 4);
        ushort[][] posSlot = new ushort[4][];
        for (int i = 0; i < 4; i++) posSlot[i] = NewProbs(64);
        ushort[] posDecoders = NewProbs(114);
        ushort[] align = NewProbs(16);
        ushort[] literals = NewProbs(0x300 << (lc + lp));
        var lenDecoder = new LenDecoder(1 << pb);
        var repLenDecoder = new LenDecoder(1 << pb);

        uint posMask = (uint)((1 << pb) - 1);
        uint lpMask = (uint)((1 << lp) - 1);
        int state = 0;
        uint rep0 = 0, rep1 = 0, rep2 = 0, rep3 = 0;
        long n = 0;

        while (n < outSize)
        {
            uint posState = (uint)n & posMask;

            if (rc.DecodeBit(isMatch, (state << 4) + (int)posState) == 0)
            {
                // Literal byte.
                byte prev = n > 0 ? output[n - 1] : (byte)0;
                int litBase = 0x300 * (int)((((uint)n & lpMask) << lc) + (uint)(prev >> (8 - lc)));
                uint symbol = 1;
                if (state >= 7)
                {
                    uint matchByte = output[n - rep0 - 1];
                    while (symbol < 0x100)
                    {
                        uint matchBit = (matchByte >> 7) & 1;
                        matchByte = (matchByte << 1) & 0xFF;
                        uint bit = rc.DecodeBit(literals, litBase + (int)(((1 + matchBit) << 8) + symbol));
                        symbol = (symbol << 1) | bit;
                        if (matchBit != bit) break;
                    }
                }
                while (symbol < 0x100)
                    symbol = (symbol << 1) | rc.DecodeBit(literals, litBase + (int)symbol);

                output[n++] = (byte)symbol;
                state = state < 4 ? 0 : (state < 10 ? state - 3 : state - 6);
                continue;
            }

            uint length;
            if (rc.DecodeBit(isRep, state) == 1)
            {
                if (rc.DecodeBit(isRepG0, state) == 0)
                {
                    if (rc.DecodeBit(isRep0Long, (state << 4) + (int)posState) == 0)
                    {
                        // Short rep: one byte from distance rep0.
                        state = state < 7 ? 9 : 11;
                        output[n] = output[n - rep0 - 1];
                        n++;
                        continue;
                    }
                }
                else
                {
                    uint distance;
                    if (rc.DecodeBit(isRepG1, state) == 0)
                    {
                        distance = rep1;
                    }
                    else
                    {
                        if (rc.DecodeBit(isRepG2, state) == 0)
                        {
                            distance = rep2;
                        }
                        else
                        {
                            distance = rep3;
                            rep3 = rep2;
                        }
                        rep2 = rep1;
                    }
                    rep1 = rep0;
                    rep0 = distance;
                }
                length = repLenDecoder.Decode(rc, posState) + 2;
                state = state < 7 ? 8 : 11;
            }
            else
            {
                rep3 = rep2;
                rep2 = rep1;
                rep1 = rep0;
                length = lenDecoder.Decode(rc, posState) + 2;
                state = state < 7 ? 7 : 10;

                uint lenToPosState = length - 2;
                if (lenToPosState > 3) lenToPosState = 3;
                uint slot = BitTree(rc, posSlot[lenToPosState], 0, 6);
                if (slot >= 4)
                {
                    int numDirectBits = (int)((slot >> 1) - 1);
                    rep0 = (2u | (slot & 1u)) << numDirectBits;
                    if (slot < 14)
                    {
                        rep0 += ReverseBitTree(rc, posDecoders, (int)(rep0 - slot - 1), numDirectBits);
                    }
                    else
                    {
                        rep0 += rc.DecodeDirectBits(numDirectBits - 4) << 4;
                        rep0 += ReverseBitTree(rc, align, 0, 4);
                    }
                }
                else
                {
                    rep0 = slot;
                }

                // End marker.
                if (rep0 == uint.MaxValue) break;
            }

            if (rep0 >= n) throw new InvalidDataException("LZMA: bad distance " + rep0 + " at " + n);

            for (uint i = 0; i < length && n < outSize; i++)
            {
                output[n] = output[n - rep0 - 1];
                n++;
            }
        }

        return output;
    }

    private static ushort[] NewProbs(int count)
    {
        var probs = new ushort[count];
        for (int i = 0; i < count; i++) probs[i] = 1024;
        return probs;
    }

    private static uint BitTree(RangeDecoder rc, ushort[] probs, int baseIndex, int numBits)
    {
        uint m = 1;
        for (int i = 0; i < numBits; i++)
            m = (m << 1) + rc.DecodeBit(probs, baseIndex + (int)m);
        return m - (1u << numBits);
    }

    private static uint ReverseBitTree(RangeDecoder rc, ushort[] probs, int baseIndex, int numBits)
    {
        uint m = 1;
        uint symbol = 0;
        for (int i = 0; i < numBits; i++)
        {
            uint bit = rc.DecodeBit(probs, baseIndex + (int)m);
            m = (m << 1) + bit;
            symbol |= bit << i;
        }
        return symbol;
    }

    private sealed class LenDecoder
    {
        private readonly ushort[] choice = NewProbs(2);
        private readonly ushort[][] low;
        private readonly ushort[][] mid;
        private readonly ushort[] high = NewProbs(256);

        public LenDecoder(int numPosStates)
        {
            low = new ushort[numPosStates][];
            mid = new ushort[numPosStates][];
            for (int i = 0; i < numPosStates; i++)
            {
                low[i] = NewProbs(8);
                mid[i] = NewProbs(8);
            }
        }

        public uint Decode(RangeDecoder rc, uint posState)
        {
            if (rc.DecodeBit(choice, 0) == 0) return BitTree(rc, low[posState], 0, 3);
            if (rc.DecodeBit(choice, 1) == 0) return 8 + BitTree(rc, mid[posState], 0, 3);
            return 16 + BitTree(rc, high, 0, 8);
        }
    }

    private sealed class RangeDecoder
    {
        private readonly byte[] data;
        private int position;
        private uint range = uint.MaxValue;
        private uint code;

        public RangeDecoder(byte[] data, int offset)
        {
            this.data = data;
            position = offset;
            for (int i = 0; i < 5; i++) code = (code << 8) | NextByte();
        }

        private uint NextByte()
        {
            // Past the end the SDK reads -1 cast to byte (0xFF); valid streams never get there.
            return position < data.Length ? data[position++] : 0u;
        }

        public uint DecodeBit(ushort[] probs, int index)
        {
            uint prob = probs[index];
            uint bound = (range >> 11) * prob;
            uint bit;
            if (code < bound)
            {
                range = bound;
                probs[index] = (ushort)(prob + ((2048 - prob) >> 5));
                bit = 0;
            }
            else
            {
                range -= bound;
                code -= bound;
                probs[index] = (ushort)(prob - (prob >> 5));
                bit = 1;
            }
            if (range < TopValue)
            {
                code = (code << 8) | NextByte();
                range <<= 8;
            }
            return bit;
        }

        public uint DecodeDirectBits(int numBits)
        {
            uint result = 0;
            for (int i = numBits; i > 0; i--)
            {
                range >>= 1;
                uint t = (code - range) >> 31;
                code -= range & (t - 1);
                result = (result << 1) | (1 - t);
                if (range < TopValue)
                {
                    code = (code << 8) | NextByte();
                    range <<= 8;
                }
            }
            return result;
        }
    }
}
