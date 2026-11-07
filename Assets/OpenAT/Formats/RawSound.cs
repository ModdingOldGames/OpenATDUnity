using System;

namespace OpenAT.Formats
{
    /// <summary>Headerless sound effects/voice (.raw): signed 16-bit little-endian mono PCM at 22050 Hz.</summary>
    public static class RawSound
    {
        public const int SampleRate = 22050;
        public const int Channels = 1;

        public static float[] ToFloat(byte[] data)
        {
            var s = new float[data.Length / 2];
            for (int i = 0; i < s.Length; i++) s[i] = BitConverter.ToInt16(data, i * 2) / 32768f;
            return s;
        }
    }
}
