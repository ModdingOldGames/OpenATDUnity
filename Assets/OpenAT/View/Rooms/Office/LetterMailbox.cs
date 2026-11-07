using System.Collections.Generic;
using System.Collections.ObjectModel;
using OpenAT.Sim;
using OpenAT.Sim.Mail;
using OpenAT.View.Boot;
using OpenAT.View.UI.Dialog;
using OpenAT.View.UI.Menus;
using UnityEngine;

namespace OpenAT.View.Rooms.Office
{
    /// <summary>
    /// The view of the in-tray for <see cref="LettersMenu"/>. The letters themselves are sim state
    /// (<see cref="Player.Mail"/>, Sim/Mail): they are collected from the first minute of the game and saved
    /// with it. This class formats them for the menu (subject = text id, body = id + 1 of the letter's
    /// group, with its arguments) and maps the menu's trash button to <see cref="DeleteLetterCommand"/>.
    /// </summary>
    public static class LetterMailbox
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() => LettersMenu.Source = p => p != null ? Letters(p) : (IList<LetterData>)System.Array.Empty<LetterData>();

        /// <summary>Kept for the rooms that call it: the mailbox no longer needs to be attached.</summary>
        public static void Attach(GameApp app) => Install();

        static InTray _cache;

        /// <summary>The formatted letters of a player, newest first (rebuilt when the in-tray changes).</summary>
        public static IList<LetterData> Letters(Player p)
        {
            if (_cache == null || _cache.Player != p || !_cache.Matches()) _cache = new InTray(p);
            return _cache;
        }

        /// <summary>Formatted copy of a mailbox; removing an entry throws the sim letter away.</summary>
        sealed class InTray : Collection<LetterData>
        {
            public readonly Player Player;
            readonly List<Letter> _source = new List<Letter>();

            public InTray(Player p)
            {
                Player = p;
                var assets = GameApp.Instance?.Assets;
                foreach (var l in p.Mail.Letters)
                {
                    _source.Add(l);
                    base.InsertItem(Count, Format(assets, l));
                }
            }

            public bool Matches()
            {
                var ls = Player.Mail.Letters;
                if (ls.Count != _source.Count) return false;
                for (int i = 0; i < ls.Count; i++) if (!ReferenceEquals(ls[i], _source[i])) return false;
                return true;
            }

            protected override void RemoveItem(int index)
            {
                var w = Player.World;
                int i = Player.Mail.Letters.IndexOf(_source[index]);
                if (w != null && i >= 0) w.Execute(new DeleteLetterCommand(Player.Index, i));
                _source.RemoveAt(index);
                base.RemoveItem(index);
            }
        }

        static LetterData Format(GameAssets assets, Letter l)
        {
            string subject = assets?.StdText.Get(l.Group, l.TextId);
            string body = assets?.StdText.Get(l.Group, l.TextId + 1);
            string sender = l.Sender;
            if (sender == null && l.SenderTextId != 0)
            {
                var raw = assets?.StdText.Get(l.Group, l.SenderTextId);
                var sargs = l.SenderArgs != null && l.SenderArgs.Length > 0 ? l.SenderArgs : l.Args;
                sender = raw != null ? DialogText.Format(raw, sargs) : null;
            }
            return new LetterData
            {
                Subject = subject != null ? DialogText.Format(subject, l.Args) : "",
                Body = body != null ? DialogText.Format(body, l.Args) : "",
                Sender = sender,
                IsFax = l.IsFax,
                Picture = l.Picture,
            };
        }
    }
}
