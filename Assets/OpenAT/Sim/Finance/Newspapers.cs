using System.Collections.Generic;
using OpenAT.Formats;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using OpenAT.Sim.Staff;

namespace OpenAT.Sim.Finance
{
    /// <summary>
    /// One newspaper headline. Fillers carry <see cref="Text"/> (from stdpaper.csv, tokens
    /// substituted); event "overrides" carry a std text id (group <c>Misc</c>) and its printf
    /// arguments, formatted by the view.
    /// </summary>
    public sealed class Headline
    {
        public string Text;
        /// <summary>Std text id (group "Misc") for override headlines, 0 for fillers.</summary>
        public int TextId;
        public object[] Args;
        /// <summary>Picture name (graphic id string, e.g. "STREIK") and the id offset added to it.</summary>
        public string Picture;
        public long PictureOffset;
        public int Priority;

        public bool IsEmpty => string.IsNullOrEmpty(Text) && TextId == 0;
        public Headline Clone() => (Headline)MemberwiseClone();
    }

    /// <summary>The kiosk's papers were refreshed (overnight).</summary>
    public sealed class NewspapersUpdated : ISimEvent
    {
        public readonly bool Interesting;
        public NewspapersUpdated(bool interesting) => Interesting = interesting;
    }

    /// <summary>
    /// Three newspapers with 10 headlines each (economy.md §16.5, sim-core.md §6.8, data-tables.md
    /// §1.9): daily filler or story chain in slot 0, token substitution, queued override headlines
    /// in the free slots, sorted by picture priority. Comparison headlines every 4th day after day 8.
    /// </summary>
    public sealed class Newspapers
    {
        public const int Papers = 3, Slots = 10;

        public readonly Headline[] Headlines = new Headline[Papers * Slots];
        public readonly Headline[] Overrides = new Headline[Papers * Slots];
        public readonly int[] CurrentChain = new int[Papers];
        public readonly string[] FlexiCity = new string[Papers];
        public readonly int[] FlexiNumber = new int[Papers];
        /// <summary>Any override made it into today's papers (the vendor is awake).</summary>
        public bool IsInteresting;

        public Newspapers() { Init(); }

        public Headline Get(int paper, int slot) => Headlines[paper * Slots + slot];

        public void Init()
        {
            for (int c = 0; c < Papers; c++) { CurrentChain[c] = 0; FlexiCity[c] = ""; FlexiNumber[c] = 0; }
            for (int c = 0; c < Headlines.Length; c++) { Headlines[c] = new Headline(); Overrides[c] = new Headline(); }
        }

        /// <summary>Queues a headline for the next papers (first free override slot of that paper; paper 0..2).</summary>
        public void AddOverride(int paper, int textId, string picture, long pictureOffset, int priority, params object[] args)
        {
            for (int c = paper * Slots; c < paper * Slots + Slots; c++)
                if (Overrides[c].IsEmpty)
                {
                    Overrides[c] = new Headline { TextId = textId, Args = args, Picture = picture, PictureOffset = pictureOffset, Priority = priority };
                    return;
                }
        }

        static int Seed(World w) => (int)(w.Clock.Date + w.Clock.StartTime);

        /// <summary>Picks today's filler or chain step per paper (original <c>ReloadHeadline</c>).</summary>
        public void Reload(World w, HeadlineTable table)
        {
            for (int c = 0; c < Headlines.Length; c++) Headlines[c] = new Headline();
            IsInteresting = false;
            var rand = new TeakRand(Seed(w));
            var newRand = new int[Papers];
            for (int c = 0; c < Papers; c++) newRand[c] = rand.Rand(table.FillerCount[c]);

            int chain = 0;
            foreach (var row in table.Rows)
            {
                int lastChain = chain;
                int paper = row.Paper;
                chain = row.Chain;
                if (paper == 0) continue;
                int z = paper - 1;
                string text = row.Text ?? "";

                if (chain == 0 && newRand[z] >= 0)
                {
                    newRand[z]--;
                    if (newRand[z] == -1) Headlines[z * Slots] = new Headline { Text = text };
                }

                if (chain != 0 && lastChain != 0 && CurrentChain[z] == 0 && newRand[z] != -10 && chain != lastChain + 1 && rand.Rand(20) == 0)
                {
                    CurrentChain[z] = chain;
                    newRand[z] = -10;
                    Headlines[z * Slots] = new Headline { Text = text };
                }

                if (CurrentChain[z] + 1 == chain && newRand[z] != -10)
                {
                    CurrentChain[z]++;
                    newRand[z] = -10;
                    Headlines[z * Slots] = new Headline { Text = text };
                }

                if (CurrentChain[(z + 1) % 3] + 1 == chain || CurrentChain[(z + 2) % 3] + 1 == chain)
                {
                    CurrentChain[z]++;
                    newRand[z] = -10;
                    Headlines[z * Slots] = new Headline { Text = text };
                }
            }

            for (int c = 0; c < Papers; c++) if (newRand[c] == -1) CurrentChain[c] = 0;
        }

        /// <summary>
        /// Token substitution in slot 0 ({city}, {{city}}, {rand(a,b)} = Rand(b+1) QUIRK, {rand}, {n} =
        /// airline (n−1) mod 4), then merge the queued overrides into free slots (original
        /// <c>InterpolateHeadline</c>).
        /// </summary>
        public void Interpolate(World w)
        {
            var rand = new TeakRand(Seed(w));
            var cities = w.Data.Cities.All;
            for (int c = 0; c < Papers; c++)
            {
                var h = Headlines[c * Slots];
                string s = h.Text ?? "";
                for (int guard = 0; guard < 100; guard++)
                {
                    int from = -1, to = -1, count = 0;
                    for (int d = s.Length - 1; d >= 0; d--)
                    {
                        if (s[d] == '}') { if (count == 0) to = d; count++; }
                        if (s[d] == '{') { count--; if (count == 0) { from = d; break; } }
                    }
                    if (from == -1 || to == -1) break;
                    string token = s.Substring(from + 1, to - from - 1);
                    string repl = null;
                    if (string.Equals(token, "city", System.StringComparison.OrdinalIgnoreCase))
                    {
                        if (cities.Count > 0) FlexiCity[c] = cities[rand.Rand(cities.Count)].Name;
                        repl = FlexiCity[c];
                    }
                    else if (string.Equals(token, "{city}", System.StringComparison.OrdinalIgnoreCase)) repl = FlexiCity[c];
                    else if (token.Length >= 5 && string.Equals(token.Substring(0, 5), "rand(", System.StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = token.Substring(5).Split(new[] { ')', ',' }, System.StringSplitOptions.RemoveEmptyEntries);
                        int a = parts.Length > 0 ? CsvTable.Atoi(parts[0]) : 0;
                        int b = parts.Length > 1 ? CsvTable.Atoi(parts[1]) : 0;
                        FlexiNumber[c] = rand.Rand(b - a + 1 + a);
                        repl = FlexiNumber[c].ToString();
                    }
                    else if (string.Equals(token, "{rand}", System.StringComparison.OrdinalIgnoreCase)) repl = FlexiNumber[c].ToString();
                    else if (CsvTable.Atoi(token) > 0) repl = w.Players[(CsvTable.Atoi(token) - 1) % Player.Count].Airline;
                    if (repl == null) break; // unknown token: the original would loop forever
                    s = s.Substring(0, from) + repl + s.Substring(to + 1);
                }
                h.Text = s;

                for (int d = 0; d < Slots; d++)
                    if (!Overrides[c * Slots + d].IsEmpty)
                        for (int e = 0; e < Slots; e++)
                            if (Headlines[c * Slots + e].IsEmpty)
                            {
                                IsInteresting = true;
                                Headlines[c * Slots + e] = Overrides[c * Slots + d];
                                Overrides[c * Slots + d] = new Headline();
                                break;
                            }
            }
        }

        /// <summary>Bubble sort per paper by priority, only between non-empty neighbours (original <c>SortByPriority</c>).</summary>
        public void SortByPriority()
        {
            for (int p = 0; p < Papers; p++)
            {
                int b = p * Slots;
                for (int c = 0; c < Slots - 1; c++)
                    if (Headlines[b + c].Priority < Headlines[b + c + 1].Priority && !Headlines[b + c].IsEmpty && !Headlines[b + c + 1].IsEmpty)
                    {
                        var tmp = Headlines[b + c]; Headlines[b + c] = Headlines[b + c + 1]; Headlines[b + c + 1] = tmp;
                        c -= 2; if (c < -1) c = -1;
                    }
            }
        }

        /// <summary>
        /// Comparison headlines (economy.md §16.5): only when Date &gt; 8 and Date % 4 == 2, one random
        /// category (seed Date + StartTime). Plus the rocket-progress headlines of the space mission.
        /// </summary>
        public void Comparison(World w)
        {
            var players = w.Players;
            int local = w.LocalPlayer;
            var bridge = w.OperationsBridge;
            int date = w.Clock.Date;
            if (date > 8 && (date & 3) == 2)
            {
                int best = -1, best2 = -1;
                var rand = new TeakRand(Seed(w));
                int Bonus(int b) => 10 + (b == local ? 10 : 0);
                int kind = rand.Rand(8);
                switch (kind)
                {
                    case 0:
                        best = BestWithTieStop(players, p => bridge.Planes(w, p).Count);
                        if (best >= 0 && bridge.Planes(w, players[best]).Count > 10)
                            AddOverride(1, 2050, "PLANES", best * 100, Bonus(best), players[best].Airline);
                        break;
                    case 1:
                        best = BestWithTieStop(players, p => p.Image);
                        for (int c = 0; c < Player.Count; c++)
                            if (!players[c].IsOut && c != best && (best2 == -1 || players[c].Image > players[best].Image)) best2 = c;
                        if (best >= 0 && best2 != -1 && players[best].Image > 100)
                            AddOverride(1, 2051, "LIEBSTE", best * 100, Bonus(best), players[best].Airline, players[best2].Airline);
                        break;
                    case 2:
                        best = BestWithTieStop(players, p => bridge.RouteCount(w, p));
                        if (best >= 0 && bridge.RouteCount(w, players[best]) > 30)
                            AddOverride(1, 2052, "ROUTEN", best * 100, Bonus(best), players[best].Airline, bridge.RouteCount(w, players[best]));
                        break;
                    case 3:
                        best = BestWithTieStop(players, p => p.Money);
                        if (best >= 0 && players[best].Money > 25000000)
                            AddOverride(1, 2053, "GELD", best * 100, Bonus(best), players[best].Airline);
                        break;
                    case 4:
                    case 5:
                    {
                        bool pilot = kind == 4;
                        int type = pilot ? WorkerType.Pilot : WorkerType.Attendant;
                        int textId = pilot ? 2080 : 2081;
                        string pic = pilot ? "PILOT" : "STEWARD";
                        var pool = w.Workers.Workers;
                        for (int c = pool.Count - 1; c >= 0; c--)
                            if (pool[c].Employer == local && pool[c].Type == type && (best == -1 || pool[c].Talent > pool[best].Talent)) best = c;
                        if (best != -1 && pool[best].Talent > 90)
                            AddOverride(2, textId, pic, 0, 20, players[local].Airline, players[local].Name, pool[best].Name);
                        else
                        {
                            int c = rand.Rand(4);
                            if (!players[c].IsOut && c != local)
                                AddOverride(2, textId, pic, 0, 20, players[c].Airline, players[c].Name, w.Workers.RandomName(pilot));
                        }
                        break;
                    }
                    case 6:
                        for (int c = 0; c < Player.Count; c++)
                            if (!players[c].IsOut && (best == -1 || players[c].Counters.Passengers > players[best].Counters.Passengers)) best = c;
                        if (best >= 0)
                            AddOverride(2, 2082, "PASSAG", 0, 10, players[best].Airline, players[best].Counters.Passengers);
                        break;
                    case 7:
                    {
                        int worst = int.MaxValue;
                        string name = null;
                        for (int c = 0; c < Player.Count; c++)
                        {
                            if (players[c].IsOut) continue;
                            var planes = bridge.Planes(w, players[c]);
                            for (int d = planes.Count - 1; d >= 0; d--)
                                if (best == -1 || planes[d].Condition < worst) { best = c; worst = planes[d].Condition; name = planes[d].Name; }
                        }
                        if (best != -1 && worst < 50)
                            AddOverride(2, 2083, "TRASHP0", (long)best << 48, 10, players[best].Airline, name);
                        break;
                    }
                }
            }

            if (w.Difficulty == Mission.Rocket)
            {
                if (date == 2) AddOverride(1, 2070, null, 0, 5);
                if (date == 3) AddOverride(0, 2071, null, 0, 5);
                if (date == 5) AddOverride(2, 2072, null, 0, 5);
                if (date == 9) AddOverride(1, 2073, null, 0, 5);
                RocketPartHeadline(w, 7, 2060, "INTRVIEW", 15);
                RocketPartHeadline(w, 127, 2061, null, 20);
                foreach (var p in players) p.Counters.LastRocketFlags = p.Counters.RocketFlags;
            }
        }

        /// <summary>First airline to complete a rocket stage gets a headline (nobody had it at the last check).</summary>
        void RocketPartHeadline(World w, int mask, int textId, string picture, int priority)
        {
            foreach (var p in w.Players) if ((p.Counters.LastRocketFlags & mask) == mask) return;
            foreach (var p in w.Players)
                if ((p.Counters.RocketFlags & mask) == mask)
                {
                    AddOverride(1, textId, picture, 0, priority + (p.Index == w.LocalPlayer ? 10 : 0), p.Airline);
                    return;
                }
        }

        /// <summary>Highest value among active airlines; the scan stops at the first tie (original).</summary>
        static int BestWithTieStop(Player[] players, System.Func<Player, long> value)
        {
            int best = -1;
            for (int c = 0; c < players.Length; c++)
            {
                if (players[c].IsOut) continue;
                if (best == -1 || value(players[c]) > value(players[best])) best = c;
                else if (value(players[c]) == value(players[best])) break;
            }
            return best;
        }
    }
}
