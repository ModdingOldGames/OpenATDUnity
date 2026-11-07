using System;
using System.Text;

namespace OpenAT.Sim.Save
{
    /// <summary>Growable little-endian byte buffer with LEB128 varints (zig-zag for signed values).</summary>
    sealed class ByteWriter
    {
        byte[] _buf = new byte[1 << 16];
        int _len;

        public int Length => _len;
        public byte[] Buffer => _buf;

        void Ensure(int n)
        {
            if (_len + n <= _buf.Length) return;
            int size = _buf.Length * 2;
            while (size < _len + n) size *= 2;
            Array.Resize(ref _buf, size);
        }

        public void Byte(byte b) { Ensure(1); _buf[_len++] = b; }

        public void UVar(ulong v)
        {
            Ensure(10);
            while (v >= 0x80) { _buf[_len++] = (byte)(v | 0x80); v >>= 7; }
            _buf[_len++] = (byte)v;
        }

        public void Var(long v) => UVar((ulong)((v << 1) ^ (v >> 63)));

        public void Fixed64(ulong v)
        {
            Ensure(8);
            for (int i = 0; i < 8; i++) { _buf[_len++] = (byte)v; v >>= 8; }
        }

        public void Fixed32(uint v)
        {
            Ensure(4);
            for (int i = 0; i < 4; i++) { _buf[_len++] = (byte)v; v >>= 8; }
        }

        public void Str(string s)
        {
            int n = Encoding.UTF8.GetByteCount(s);
            UVar((ulong)n);
            Ensure(n);
            Encoding.UTF8.GetBytes(s, 0, s.Length, _buf, _len);
            _len += n;
        }

        /// <summary>Raw copy of a primitive array (any rank).</summary>
        public void Block(Array a, int byteCount)
        {
            UVar((ulong)byteCount);
            Ensure(byteCount);
            System.Buffer.BlockCopy(a, 0, _buf, _len, byteCount);
            _len += byteCount;
        }
    }

    sealed class ByteReader
    {
        readonly byte[] _buf;
        int _pos;
        readonly int _end;

        public ByteReader(byte[] buf, int start, int end) { _buf = buf; _pos = start; _end = end; }

        public int Position => _pos;

        void Need(int n)
        {
            if (_pos + n > _end) throw new SaveException("save file truncated");
        }

        public byte Byte() { Need(1); return _buf[_pos++]; }

        public ulong UVar()
        {
            ulong v = 0;
            int shift = 0;
            while (true)
            {
                Need(1);
                byte b = _buf[_pos++];
                v |= (ulong)(b & 0x7F) << shift;
                if (b < 0x80) return v;
                shift += 7;
                if (shift > 63) throw new SaveException("bad varint");
            }
        }

        public long Var() { ulong u = UVar(); return (long)(u >> 1) ^ -(long)(u & 1); }

        public int Int() => checked((int)UVar());

        public ulong Fixed64()
        {
            Need(8);
            ulong v = 0;
            for (int i = 0; i < 8; i++) v |= (ulong)_buf[_pos++] << (8 * i);
            return v;
        }

        public uint Fixed32()
        {
            Need(4);
            uint v = 0;
            for (int i = 0; i < 4; i++) v |= (uint)_buf[_pos++] << (8 * i);
            return v;
        }

        public string Str()
        {
            int n = Int();
            Need(n);
            var s = Encoding.UTF8.GetString(_buf, _pos, n);
            _pos += n;
            return s;
        }

        /// <summary>Raw block into a primitive array; the sizes must match (else the block is skipped and false returned).</summary>
        public bool Block(Array target, int expectedBytes)
        {
            int n = Int();
            Need(n);
            bool ok = target != null && n == expectedBytes;
            if (ok) System.Buffer.BlockCopy(_buf, _pos, target, 0, n);
            _pos += n;
            return ok;
        }
    }
}
