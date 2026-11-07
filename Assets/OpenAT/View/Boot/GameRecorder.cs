using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace OpenAT.View.Boot
{
    /// <summary>
    /// Records the game itself to an mp4 (OpenAT dev tool for videos, docs/VIRTUALPAD.md "Recording"): the rendered frames
    /// at the window's resolution, read back from the GPU and piped into ffmpeg at a constant frame rate, and the
    /// game's own sound mix (what the audio listener hears: effects, voices, ambience, and music only if it is on)
    /// written to a WAV file. On stop both are joined into the mp4. Nothing of the desktop, other windows or a
    /// microphone can get in. Started and stopped through the virtual pad channel (<c>record start file.mp4 [fps]</c>,
    /// <c>record stop</c>, <c>record status</c>). Needs ffmpeg (PATH, or the environment variable OPENAT_FFMPEG).
    /// Frames are timed by the real clock: a late frame is repeated, so the video keeps the game's real speed and
    /// stays in step with the sound.
    /// </summary>
    public sealed class GameRecorder
    {
        public bool Recording { get; private set; }
        public string Status { get; private set; } = "idle";

        string _out, _tmpVideo, _wav;
        int _fps, _w, _h;
        float _start;
        long _written;           // video frames written
        Process _ffmpeg;
        Stream _pipe;
        Thread _writer;
        BlockingCollection<(byte[] data, int count)> _frames;
        int _owed;               // frames a full queue could not take; added to the next one
        AudioTap _tap;

        static string Ffmpeg => Environment.GetEnvironmentVariable("OPENAT_FFMPEG") is string f && f.Length > 0 ? f : "ffmpeg";

        public string Start(RenderTexture rt, string path, int fps)
        {
            if (Recording) return "error already recording to " + _out;
            if (rt == null) return "error no frame";
            _fps = Mathf.Clamp(fps, 10, 60);
            _w = rt.width & ~1; _h = rt.height & ~1;   // even sizes for yuv420p
            _out = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(_out));
            _tmpVideo = _out + ".video.mp4";
            _wav = Path.ChangeExtension(_out, ".wav");
            try
            {
                var psi = new ProcessStartInfo(Ffmpeg,
                    $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {rt.width}x{rt.height} -r {_fps} -i - " +
                    $"-vf \"vflip,crop={_w}:{_h}:0:0{Pad()}\" -c:v libx264 -preset veryfast -crf 16 -pix_fmt yuv420p \"{_tmpVideo}\"")
                { UseShellExecute = false, RedirectStandardInput = true, CreateNoWindow = true };
                _ffmpeg = Process.Start(psi);
                _pipe = _ffmpeg.StandardInput.BaseStream;
            }
            catch (Exception e) { return "error cannot start ffmpeg (" + Ffmpeg + "): " + e.Message; }

            _tap = AudioTap.Attach();
            if (_tap == null) Debug.LogWarning("OpenAT recorder: no audio listener, recording without sound");
            else _tap.Begin(_wav);
            _written = 0;
            _owed = 0;
            _frames = new BlockingCollection<(byte[], int)>(24);
            _start = Time.realtimeSinceStartup;
            Recording = true;
            _writer = new Thread(WriteLoop) { IsBackground = true, Name = "OpenAT recorder" };
            _writer.Start();
            Status = "recording " + _out + " " + _w + "x" + _h + " @" + _fps;
            Debug.Log("OpenAT recorder: " + Status);
            return "ok " + Status;
        }

        /// <summary>The game's canvas can be a few pixels narrower than the window (1916 at 1080p): pad to the window's size.</summary>
        string Pad()
        {
            int tw = UnityEngine.Screen.width & ~1, th = UnityEngine.Screen.height & ~1;
            if (tw < _w || th < _h || tw - _w > 16 || th - _h > 16 || (tw == _w && th == _h)) return "";
            return $",pad={tw}:{th}:(ow-iw)/2:(oh-ih)/2";
        }

        void WriteLoop()
        {
            try
            {
                foreach (var (data, count) in _frames.GetConsumingEnumerable())
                    for (int i = 0; i < count; i++) _pipe.Write(data, 0, data.Length);
            }
            catch (Exception e) { Debug.LogError("OpenAT recorder: writing frames failed: " + e.Message); }
        }

        /// <summary>After a frame is rendered: reads it back and queues it as many times as the real clock needs.</summary>
        public void Capture(RenderTexture rt)
        {
            if (!Recording || rt == null) return;
            float t = Time.realtimeSinceStartup - _start;
            long due = (long)(t * _fps) + 1;   // frames that should exist after this one
            if (due <= _written) return;
            int count = (int)(due - _written);
            _written = due;
            AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, req =>
            {
                if (req.hasError || !Recording) return;
                var data = req.GetData<byte>().ToArray();
                // A full queue (slow encoder) never stalls the game: the frame's slots go to the next frame.
                if (_frames.TryAdd((data, count + _owed))) _owed = 0;
                else _owed += count;
            });
        }

        public string Stop()
        {
            if (!Recording) return "error not recording";
            Recording = false;
            AsyncGPUReadback.WaitAllRequests();
            float seconds = Time.realtimeSinceStartup - _start;
            _tap?.End();
            _frames.CompleteAdding();
            Status = "finishing " + _out;
            var outPath = _out; var tmp = _tmpVideo; var wav = _wav; var ff = _ffmpeg; var pipe = _pipe; var writer = _writer;
            bool sound = _tap != null;
            new Thread(() =>
            {
                try
                {
                    writer.Join();
                    pipe.Close();
                    ff.WaitForExit();
                    if (sound)
                    {
                        var mux = Process.Start(new ProcessStartInfo(Ffmpeg,
                            $"-y -loglevel error -i \"{tmp}\" -i \"{wav}\" -c:v copy -c:a aac -b:a 192k -shortest \"{outPath}\"")
                            { UseShellExecute = false, CreateNoWindow = true });
                        mux.WaitForExit();
                        if (mux.ExitCode == 0) File.Delete(tmp);
                    }
                    else
                    {
                        if (File.Exists(outPath)) File.Delete(outPath);
                        File.Move(tmp, outPath);
                    }
                    Status = "done " + outPath + " (" + seconds.ToString("0.0") + " s" + (sound ? ", sound in " + Path.GetFileName(wav) + " too" : ", no sound") + ")";
                }
                catch (Exception e) { Status = "error finishing: " + e.Message; }
                Debug.Log("OpenAT recorder: " + Status);
            }) { IsBackground = true }.Start();
            return "ok stopping, " + seconds.ToString("0.0") + " s; ask 'record status' until it says done";
        }

        /// <summary>Taps the final mix on the audio listener's object and writes it to a 16-bit WAV file.</summary>
        sealed class AudioTap : MonoBehaviour
        {
            FileStream _file;
            readonly object _lock = new object();
            int _channels = 2, _rate;
            long _samples;
            byte[] _buf = new byte[0];

            public static AudioTap Attach()
            {
                var listener = FindAnyObjectByType<AudioListener>();
                if (listener == null) return null;
                return listener.GetComponent<AudioTap>() ?? listener.gameObject.AddComponent<AudioTap>();
            }

            public void Begin(string path)
            {
                lock (_lock)
                {
                    _rate = AudioSettings.outputSampleRate;
                    _channels = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;
                    _file = new FileStream(path, FileMode.Create, FileAccess.Write);
                    _file.Write(new byte[44], 0, 44);   // header written at the end
                    _samples = 0;
                }
            }

            void OnAudioFilterRead(float[] data, int channels)
            {
                lock (_lock)
                {
                    if (_file == null) return;
                    _channels = channels;
                    if (_buf.Length < data.Length * 2) _buf = new byte[data.Length * 2];
                    for (int i = 0; i < data.Length; i++)
                    {
                        short v = (short)Mathf.Clamp(Mathf.RoundToInt(data[i] * 32767f), -32768, 32767);
                        _buf[2 * i] = (byte)v; _buf[2 * i + 1] = (byte)(v >> 8);
                    }
                    _file.Write(_buf, 0, data.Length * 2);
                    _samples += data.Length;
                }
            }

            public void End()
            {
                lock (_lock)
                {
                    if (_file == null) return;
                    long bytes = _samples * 2;
                    _file.Seek(0, SeekOrigin.Begin);
                    var w = new BinaryWriter(_file);
                    w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' }); w.Write((int)(36 + bytes));
                    w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
                    w.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' }); w.Write(16); w.Write((short)1); w.Write((short)_channels);
                    w.Write(_rate); w.Write(_rate * _channels * 2); w.Write((short)(_channels * 2)); w.Write((short)16);
                    w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' }); w.Write((int)bytes);
                    w.Flush();
                    _file.Close();
                    _file = null;
                }
            }
        }
    }
}
