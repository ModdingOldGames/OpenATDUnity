using System;
using System.Collections.Generic;
using OpenAT.Sim.Core;
using OpenAT.Sim.Fleet;

namespace OpenAT.Sim.Mail
{
    /// <summary>
    /// One letter or fax in an airline's in-tray (sim-core.md §5.2 "letters"; rooms-and-ui-2 §4.7). The text
    /// is kept as a reference into the install's texts (group, id, printf arguments) and formatted by the
    /// view when shown, so no original text is stored in the save. Subject = <see cref="TextId"/>, body =
    /// <see cref="TextId"/> + 1 of <see cref="Group"/>.
    /// </summary>
    public sealed class Letter
    {
        public string Group;
        public int TextId;
        /// <summary>printf arguments (strings and numbers only).</summary>
        public object[] Args = Array.Empty<object>();
        /// <summary>A fax (shown on the fax sheet) instead of a letter.</summary>
        public bool IsFax;
        /// <summary>Fax picture name (letter.gli) or null.</summary>
        public string Picture;
        /// <summary>Optional sender line; null = none.</summary>
        public string Sender;
        /// <summary>Sender line as a text id of <see cref="Group"/> (0 = none); used when <see cref="Sender"/> is null.</summary>
        public int SenderTextId;
        /// <summary>printf arguments of the sender line (null/empty = <see cref="Args"/>).</summary>
        public object[] SenderArgs = Array.Empty<object>();
        /// <summary>Day counter and clock time when it arrived.</summary>
        public int Date, Time;

        Letter() { }

        public Letter(string group, int textId, object[] args)
        {
            Group = group;
            TextId = textId;
            Args = args ?? Array.Empty<object>();
        }
    }

    /// <summary>
    /// An airline's in-tray, newest first. Fed from <see cref="LetterSent"/> from the first minute of the
    /// game (not only once the office has been opened) and saved with the game. The original holds 25
    /// letters; when full the oldest is dropped.
    /// </summary>
    public sealed class Mailbox
    {
        public const int Capacity = 25;
        public readonly List<Letter> Letters = new List<Letter>();

        public int Count => Letters.Count;

        public void Add(Letter l)
        {
            Letters.Insert(0, l);
            while (Letters.Count > Capacity) Letters.RemoveAt(Letters.Count - 1);
        }
    }

    /// <summary>Puts the <see cref="LetterSent"/> events of human airlines into their <see cref="Player.Mail"/>.</summary>
    public static class MailModule
    {
        public static void Register(World w)
        {
            w.SetupSteps.Add(new WelcomeLetters());
            w.Events.Subscribe<LetterSent>(e =>
            {
                var p = e.Player;
                if (p == null || p.IsComputer || p.World != w) return;
                p.Mail.Add(new Letter(e.Group, e.TextId, Sanitize(e.Args)) { Date = w.Clock.Date, Time = w.Clock.Time });
                w.Events.Publish(new LetterArrived(p));
            });
            w.Events.Subscribe<Finance.WorstAdLetter>(e => WorstAd(w, e));
        }

        /// <summary>
        /// The joke letter about a rival's cheapest ad (economy.md §10 tier 0): subject/body <c>Lett</c> 9900/9901
        /// (company), 9910/9911 (route: from, to, airline), 9920/9921 (everything), signed with +2 (name, airline).
        /// </summary>
        static void WorstAd(World w, Finance.WorstAdLetter e)
        {
            var to = e.Recipient;
            var from = e.Advertiser;
            if (to == null || from == null || to.IsComputer || to.World != w) return;
            object[] args = { from.Airline };
            if (e.Type == Finance.AdType.Route)
            {
                string a = "", b = "";
                foreach (var r in w.Data.Routes)
                    if (r.Id == e.RouteId)
                    {
                        a = w.Data.Cities.Find(r.FromCity)?.Name ?? "";
                        b = w.Data.Cities.Find(r.ToCity)?.Name ?? "";
                        break;
                    }
                args = new object[] { a, b, from.Airline };
            }
            int id = e.LetterBase;
            to.Mail.Add(new Letter("Lett", id, args)
            {
                SenderTextId = id + 2, SenderArgs = new object[] { from.Name, from.Airline },
                Date = w.Clock.Date, Time = w.Clock.Time,
            });
            w.Events.Publish(new LetterArrived(to));
        }

        /// <summary>
        /// New game (sim-core.md §3.6): every human airline starts with the welcome letter (Lett 1500 subject,
        /// 1501 body, 1502 sender); in the rocket mission (5) a second one (Lett 1200..1202) on top of it.
        /// </summary>
        sealed class WelcomeLetters : ISetupStep
        {
            public string Name => "mail.welcome";
            public int Order => 900;

            public void Apply(World w, NewGameSettings s)
            {
                foreach (var p in w.Players)
                {
                    if (p.IsComputer || p.IsOut) continue;
                    p.Mail.Add(new Letter("Lett", 1500, null) { SenderTextId = 1502, Date = w.Clock.Date, Time = w.Clock.Time });
                    if (w.Difficulty == OpenAT.Sim.Mission.Rocket)
                        p.Mail.Add(new Letter("Lett", 1200, null) { SenderTextId = 1202, Date = w.Clock.Date, Time = w.Clock.Time });
                }
            }
        }

        /// <summary>Keeps the arguments saveable: numbers and strings stay, anything else becomes its text.</summary>
        static object[] Sanitize(object[] args)
        {
            if (args == null || args.Length == 0) return Array.Empty<object>();
            var r = new object[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                r[i] = a == null || a is string || a is int || a is long || a is short || a is byte || a is double || a is float || a is bool ? a : a.ToString();
            }
            return r;
        }
    }

    /// <summary>A letter was filed into a human airline's in-tray.</summary>
    public sealed class LetterArrived : ISimEvent
    {
        public readonly Player Player;
        public LetterArrived(Player p) => Player = p;
    }

    /// <summary>Throws a letter away (letters menu, trash button).</summary>
    public sealed class DeleteLetterCommand : ICommand<World>
    {
        public readonly int PlayerIndex, Index;
        public DeleteLetterCommand(int player, int index) { PlayerIndex = player; Index = index; }

        public CommandResult Validate(World w)
        {
            if (PlayerIndex < 0 || PlayerIndex >= Player.Count) return CommandResult.Fail("bad-player");
            var m = w.Players[PlayerIndex].Mail;
            if (Index < 0 || Index >= m.Count) return CommandResult.Fail("no-letter");
            return CommandResult.Success;
        }

        public void Apply(World w) => w.Players[PlayerIndex].Mail.Letters.RemoveAt(Index);
    }
}
