using System;
using System.IO;
using System.Linq;
using OpenAT.Formats;
using Xunit;

namespace OpenAT.Tests
{
    public class SmackerTests
    {
        [Fact]
        public void Open_RejectsNonSmacker()
        {
            Assert.Throws<InvalidDataException>(() => SmackerVideo.Open(new byte[] { (byte)'x', (byte)'x' }));
            var bad = new byte[SmackerVideo.HeaderSize];
            Assert.Throws<InvalidDataException>(() => SmackerVideo.Open(bad));
        }

        [Fact]
        public void Open_MinimalSyntheticFile()
        {
            // 4x4, one frame, all four trees absent (one zero byte), frame of one zero byte: a single
            // mono block with colours 0/0 -> all index 0, palette untouched (opaque black).
            var d = new byte[SmackerVideo.HeaderSize + 5 + 1 + 1];
            "SMK2".Select(c => (byte)c).ToArray().CopyTo(d, 0);
            BitConverter.GetBytes(4).CopyTo(d, 4);
            BitConverter.GetBytes(4).CopyTo(d, 8);
            BitConverter.GetBytes(1).CopyTo(d, 12);
            BitConverter.GetBytes(100).CopyTo(d, 16);
            BitConverter.GetBytes(1).CopyTo(d, 52);
            BitConverter.GetBytes(4).CopyTo(d, SmackerVideo.HeaderSize); // frame size 1 rounded to 4 -> clamped
            var v = SmackerVideo.Open(d);
            Assert.Equal(10.0, v.FrameRate, 6);
            Assert.True(v.DecodeNextFrame());
            Assert.All(v.Indices, b => Assert.Equal(0, b));
            Assert.Equal(0xFF000000u, v.Palette[0]);
            Assert.False(v.DecodeNextFrame());
        }

        [Fact]
        public void DecodesCharacterAnimation()
        {
            var gi = GameInstall.TryLocate();
            if (gi == null) return;
            var path = gi.Find(GameInstall.Folders.Video, "Alien_.smk");
            if (path == null) return;

            var v = SmackerVideo.Open(path);
            Assert.Equal(168, v.Width);
            Assert.Equal(168, v.Height);
            Assert.Equal(37, v.FrameCount);
            Assert.False(v.HasAudio);
            int n = 0;
            while (v.DecodeNextFrame()) n++;
            Assert.Equal(v.FrameCount, n);
            Assert.Equal(v.Width * v.Height, v.Indices.Length);
            var rgba = new byte[v.Width * v.Height * 4];
            v.ToRgba(rgba, true);
            Assert.Contains(rgba, b => b != 0);

            // Rewind reproduces frame 0; Loop wraps around.
            v.Rewind();
            Assert.True(v.DecodeNextFrame());
            var first = (byte[])v.Indices.Clone();
            v.Loop = true;
            for (int i = 1; i < v.FrameCount; i++) Assert.True(v.DecodeNextFrame());
            Assert.True(v.DecodeNextFrame());
            Assert.Equal(0, v.CurrentFrame);
            Assert.Equal(first, v.Indices);
        }

        [Fact]
        public void DecodesAllCharacterVideos()
        {
            var gi = GameInstall.TryLocate();
            if (gi == null) return;
            int files = 0;
            foreach (var rel in gi.List(GameInstall.Folders.Video, ".smk"))
            {
                var v = SmackerVideo.Open(gi.ReadAllBytes(rel));
                int n = 0;
                while (v.DecodeNextFrame()) n++;
                Assert.Equal(v.FrameCount, n);
                files++;
            }
            Assert.True(files > 0);
        }

        [Fact]
        public void DecodesIntroWithAudio()
        {
            var gi = GameInstall.TryLocate();
            if (gi == null) return;
            var path = gi.Find(GameInstall.Folders.Intro, "intro.smk");
            if (path == null) return;

            var v = SmackerVideo.Open(path);
            Assert.Equal(640, v.Width);
            Assert.Equal(136, v.Height);
            Assert.True(v.IsYDoubled);
            Assert.Equal(272, v.DisplayHeight);
            Assert.Equal(1248, v.FrameCount);
            Assert.Equal(15.0, v.FrameRate, 1);
            Assert.True(v.HasAudio);
            Assert.Equal(44100, v.SampleRate);
            Assert.Equal(2, v.Channels);
            Assert.Equal(16, v.Bits);

            long samples = 0;
            for (int i = 0; i < 60; i++)
            {
                Assert.True(v.DecodeNextFrame());
                Assert.Equal(0, v.LastAudio.Length % v.Channels);
                samples += v.LastAudio.Length;
            }
            Assert.True(samples > 0);
        }
    }
}
