using System;
using System.IO;
using OpenAT.Sim;
using OpenAT.Sim.Stats;
using OpenAT.View.Boot;
using UnityEngine;

namespace OpenAT.View.Session
{
    /// <summary>
    /// The highscore table on disk (sim-core.md §5.4): <c>highscores.txt</c> in the save folder
    /// (<see cref="SaveSlots.Folder"/>), six plain lines. Not part of any save game. A small component follows
    /// <see cref="GameApp.Session"/> and offers every <see cref="HighscoreOffered"/> of the local player.
    /// </summary>
    public sealed class HighscoreStore : MonoBehaviour
    {
        public const string FileName = "highscores.txt";

        static HighscoreTable _table;

        public static string PathOf => System.IO.Path.Combine(SaveSlots.Folder, FileName);

        /// <summary>The table (read from disk on first use; empty when missing or unreadable).</summary>
        public static HighscoreTable Table
        {
            get
            {
                if (_table != null) return _table;
                try { _table = File.Exists(PathOf) ? HighscoreTable.Parse(File.ReadAllText(PathOf)) : new HighscoreTable(); }
                catch (Exception e) { Debug.LogWarning("OpenAT highscores: cannot read " + PathOf + ": " + e.Message); _table = new HighscoreTable(); }
                return _table;
            }
        }

        /// <summary>Offers a score; writes the file when the table changed.</summary>
        public static void Offer(string name, uint session, long score)
        {
            if (!Table.Offer(name, session, score)) return;
            try
            {
                Directory.CreateDirectory(SaveSlots.Folder);
                File.WriteAllText(PathOf, Table.Serialize());
            }
            catch (Exception e) { Debug.LogWarning("OpenAT highscores: cannot write " + PathOf + ": " + e.Message); }
        }

        World _world;
        IDisposable _sub;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            var go = new GameObject("OpenAT.Highscores");
            DontDestroyOnLoad(go);
            go.AddComponent<HighscoreStore>();
        }

        void Update()
        {
            var w = GameApp.Instance?.Session?.World;
            if (w == _world) return;
            _sub?.Dispose();
            _sub = null;
            _world = w;
            if (w == null) return;
            _sub = w.Events.Subscribe<HighscoreOffered>(e =>
            {
                if (e.Player != null && e.Player.Index == w.LocalPlayer) Offer(e.Player.Name, e.Session, e.Score);
            });
        }
    }
}
