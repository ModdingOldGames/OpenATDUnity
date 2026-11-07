using System;
using OpenAT.Sim.Core;

namespace OpenAT.Sim.Hall
{
    /// <summary>The per-tick hall sequence (sim-core.md §2.3 step 3.1-3.2 and step 8).</summary>
    public sealed partial class HallState
    {
        /// <summary>TimeSlice seen by the deferred spawn phase (see <see cref="HallSystem"/>).</summary>
        internal int PendingSpawnSlice = -1;
        internal bool PendingSpawnAllowed;

        /// <summary>Step 3.1: player waypoints and steering (08:00-18:00, not after call-it-a-day).</summary>
        internal void WalkPlayers()
        {
            int t = Clock.Time;
            if (t < 8 * 60000 || t > 18 * 60000 || World.CallItADay) return;
            if (!OpenAT.Sim.Tutorial.TutorialEngine.FiguresMayMove(World)) return;   // tutorial: nobody walks before step 1100
            for (int c = 0; c < Player.Count; c++)
            {
                if ((t & 255) == 0 && Players[c].WalkActive) UpdateWaypoints(c);
                UpdateWalkingDirection(c);
            }
        }

        /// <summary>Step 3.2: queue, persons, doors (not before 08:00, not in the night phase).</summary>
        internal void StepPersons()
        {
            int t = Clock.Time;
            if (t < 8 * 60000 || (t >= 18 * 60000 && Clock.State == DayState.Night)) return;
            if (!World.CallItADay) PumpQueue();
            DoOneStep();
            if (!World.CallItADay) PumpDoors();
        }

        void PumpQueue()
        {
            if (Queue.TakeDue(Clock.TimeSlice, out var e))
                Persons.Add(CreatePerson(e.ClanId, e.Position, e.Reason, e.FlightAirline, e.FlightPlaneId, e.FlightPlaneIndex, e.Mood, e.FirstClass));
        }

        /// <summary>All persons one step (PERSONS::DoOneStep), then the depth sort.</summary>
        void DoOneStep()
        {
            if (!AnyPersonsInAirport && Clock.Time < 9 * 60000) return;
            if (World.CallItADay)
            {
                // Fast version after call-it-a-day: only players in the hall walk.
                for (int c = 0; c < Player.Count; c++)
                    if (!World.Players[c].IsOut && Players[c].InHall && PlayerPerson(c) is Person pp) PlayerStep(pp);
                return;
            }
            int waiting = Clock.TimeSlice & 0xFF;
            int t = Clock.Time;
            int n = Persons.Count;
            for (int i = 0; i < n; i++)
            {
                var p = Persons[i];
                if (!p.Alive) continue;
                if (p.MoodCountdown > 0)
                {
                    p.MoodCountdown--;
                    if (p.MoodCountdown == Mood.CountStart >> 1) p.MoodCountdown -= CosmeticRand().Rand(30);
                    if (p.MoodCountdown < 0) p.MoodCountdown = 0;
                }
                var k = Clan(p.ClanId);
                if (k == null) { p.Alive = false; continue; }
                if (ClanType.IsCustomer(k.Type))
                {
                    if (t <= 8 * 60000 || t >= 18 * 60000 || (p.State & PState.Talking) != 0) continue;
                    if (p.State == (PState.ExShop | PState.BrowseFlag | PState.WaitFlag))
                    {
                        p.WaitCount -= 5;
                        if (p.WaitCount < 20) p.State = PState.ExShop | PState.BrowseFlag;
                        continue;
                    }
                    if ((p.State & PState.WaitFlag) != 0 && (p.State & ~PState.WaitFlag & ~PState.BrowseFlag) != PState.CheckingIn)
                    {
                        if (p.WaitCount <= 250) p.WaitCount++;
                    }
                    else if (p.WaitCount > 10) p.WaitCount -= 10;

                    if ((p.State & PState.WaitFlag) == 0 || ((waiting + p.ClanId) & 3) == 0)
                    {
                        if (t > 9 * 60000 || p.Reason != Reason.Waypoint)
                        {
                            CustomerStep(p);
                            if (p.Running) CustomerStep(p);
                        }
                        if (p.State == PState.Leaving)
                        {
                            PAnd(p.Position.X / 44, p.Position.Y / 22 + 5, ~Plate.Occupied);
                            p.Alive = false;
                        }
                    }
                }
                else if (k.IsPlayer)
                {
                    int pn = p.State;
                    var hp = Players[pn];
                    if (!hp.InHall || World.Players[pn].IsOut) continue;
                    if (!OpenAT.Sim.Tutorial.TutorialEngine.FiguresMayMove(World)) continue;
                    if (hp.OnToilet != 0) { ToiletPause(pn, hp, p); continue; }   // held at the toilet door
                    if (hp.TalkPartner != 0 && World.Players[pn].IsComputer) continue;   // stands still while talked to
                    if (hp.PhonePartner != 0 && World.Players[pn].IsComputer && World.Options.ComputerPhonePose) continue;   // addition: holds still on the phone
                    // Stick walking (OpenAT): the pace decides how many steps this tick takes (0 to 3).
                    int paced = StickSteps(hp, p);
                    if (paced != 0) PlayerStep(p);
                    bool walkway = p.Position.Y / 22 + 5 == 13 && (P(p.Position.X / 44, p.Position.Y / 22 + 5) & 240) == 80;
                    if (hp.IsStuck == 0 && p.Running && !walkway && hp.InHall && paced != 1)
                    {
                        UpdateWalkingDirection(pn);
                        int ph = p.Phase;
                        PlayerStep(p);
                        if (hp.IsStuck == 0 && p.Dir < 8) p.Phase = ph;
                        if (paced == 3 && hp.IsStuck == 0 && hp.InHall)
                        {
                            UpdateWalkingDirection(pn);
                            PlayerStep(p);
                            if (hp.IsStuck == 0 && p.Dir < 8) p.Phase = ph;
                        }
                    }
                    if (hp.Koffein > 0)
                    {
                        for (int r = 0; r < 2; r++) if (hp.InHall) { UpdateWalkingDirection(pn); PlayerStep(p); }
                    }
                    UpdateIdle(pn);
                    if (pn == World.LocalPlayer) hp.DidntMove++;
                }
                else if (k.Type == ClanType.Animation)
                {
                    if (p.State == PState.Leaving)
                    {
                        if (k.Group == 10) PAnd(p.Position.X / 44, (p.Position.Y - 5000) / 22, ~Plate.Special);
                        p.Alive = false;
                    }
                    else AnimationStep(p, k);
                }
            }
            DepthSort();
        }

        /// <summary>
        /// The toilet pause of a human figure (rooms-and-ui-2 §4.19, MENU_WC_F/M): after 20 ticks the flush
        /// (<see cref="HallToiletFlush"/>: sound, perfume in the ladies'); after 80 ticks (40 for player 0 in the
        /// gents') the figure walks out of the doorway — it is already in "leaving a room" mode from its
        /// arrival — to a point 44 px in front of the room's approach rune.
        /// </summary>
        void ToiletPause(int pn, HallPlayer hp, Person me)
        {
            hp.OnToiletTicks++;
            if (hp.OnToiletTicks == 21) World.Events.Publish(new HallToiletFlush(pn, hp.OnToilet));
            int end = hp.OnToilet == RoomIds.WcM && pn == 0 ? 40 : 80;
            if (hp.OnToiletTicks <= end) return;
            int room = hp.OnToilet;
            hp.OnToilet = 0;
            hp.OnToiletTicks = 0;
            var at = RandomRune(RuneId.ToShop, room, null);
            if (at == null) return;
            me.Target = at.Value + new Pt(0, 44);
            if (me.StatePar == 0) { me.StatePar = -1; hp.ExRoom = room; }
            World.Events.Publish(new HallToiletFlush(pn, room, done: true));
        }

        /// <summary>Sabotage effect objects (glue, stink bomb), §6.6.</summary>
        void AnimationStep(Person p, ClanDef k)
        {
            bool up = p.Position.Y >= 4000;
            var a = up ? new Pt(p.Position.X / 44, (p.Position.Y - 5000) / 22) : new Pt(p.Position.X / 44, p.Position.Y / 22 + 5);
            if (k.Group == 10) POr(a.X, a.Y, Plate.Special);
            else if (k.Group == 20 && p.WaitCount > 0)
            {
                for (int x = -1; x <= 1; x++)
                {
                    POr(a.X + x, a.Y, 1);
                    if (a.Y > 5) POr(a.X + x, a.Y - 1, 1);
                    if (a.Y < 15) POr(a.X + x, a.Y + 1, 1);
                }
                if (--p.WaitCount == 0)
                    for (int x = -1; x <= 1; x++)
                    {
                        PAnd(a.X + x, a.Y, ~1);
                        if (a.Y > 5) PAnd(a.X + x, a.Y - 1, ~1);
                        if (a.Y < 15) PAnd(a.X + x, a.Y + 1, ~1);
                    }
            }
            p.ScreenPos = up ? new Pt(p.Position.X - (p.Position.Y - 5000) / 2 + 86, p.Position.Y - 5000 + 93) : new Pt(p.Position.X - p.Position.Y / 2 + 86, p.Position.Y + 220);
        }

        /// <summary>Stable depth sort; removed persons drop out (§3.5).</summary>
        public void DepthSort()
        {
            int w = 0;
            for (int i = 0; i < Persons.Count; i++) if (Persons[i].Alive) Persons[w++] = Persons[i];
            Persons.RemoveRange(w, Persons.Count - w);
            for (int i = 1; i < Persons.Count; i++)
            {
                var v = Persons[i]; int j = i - 1;
                while (j >= 0 && Person.Compare(Persons[j], v) > 0) { Persons[j + 1] = Persons[j]; j--; }
                Persons[j + 1] = v;
            }
        }

        // ------------------------------------------------------------------ spawning (§6.6, sim-core §2.3 step 8)

        /// <summary>
        /// The original spawns after the clock advanced in the same tick, with the TimeSlice from before
        /// its increment. World.Tick runs systems before the clock, so the hall records that slice at the
        /// end of a tick and runs the spawn phase at the start of the next one; the order of RNG draws is
        /// the same.
        /// </summary>
        internal void RecordSpawnPhase()
        {
            PendingSpawnSlice = Clock.TimeSlice;
            PendingSpawnAllowed = !World.Paused && !World.CallItADay;
        }

        internal void RunPendingSpawnPhase()
        {
            if (PendingSpawnSlice < 0) return;
            int ts = PendingSpawnSlice;
            bool allowed = PendingSpawnAllowed;
            PendingSpawnSlice = -1;
            int t = Clock.Time;
            if (!allowed || Clock.Frozen || t < 8 * 60000 || t > 18 * 60000) { if (t == GameClock.DayStart) Queue.Clear(); return; }
            if ((ts & 63) == 0) AddNewShoppers();
            if ((ts & 7) == 0 && Players[World.LocalPlayer].InHall) TryMoods();
            if (((ts + 16) & 31) == 0) Flights?.AddNewPassengers(World, this);
            // TimeSlice is reset at exactly 09:00 and the queue is cleared with it.
            if (t == GameClock.DayStart) Queue.Clear();
        }

        /// <summary>
        /// Mouse position over the hall in hall pixels (UI input, set by the hall view every frame; null when
        /// the mouse is elsewhere). Only used for the hover thought bubbles.
        /// </summary>
        public Pt? Hover;

        /// <summary>Sympathy of computer player a towards b (−100..100); the AI domain may provide it.</summary>
        public Func<int, int, int> Sympathy = (a, b) => 0;

        int SympathyMood(int a, int b)
        {
            int s = Sympathy(a, b);
            return s > 50 ? 0 : s > 10 ? 1 : s > -10 ? 2 : s > -50 ? 3 : 4;
        }

        void Bubble(Person p, int mood)
        {
            p.Mood = mood;
            p.MoodCountdown = p.MoodCountdown != 0 ? Mood.CountStart - 16 : Mood.CountStart + CosmeticRand().Rand(15);
        }

        /// <summary>Hover keeps a bubble at full length (§6.7).</summary>
        static void Refresh(Person p, int fresh)
        {
            if (p.MoodCountdown != 0)
            {
                if (p.MoodCountdown < 16) p.MoodCountdown = Mood.CountStart - p.MoodCountdown;
                else if (p.MoodCountdown < Mood.CountStart - 16) p.MoodCountdown = Mood.CountStart - 16 - 1;
            }
            else p.MoodCountdown = fresh;
        }

        /// <summary>Thought bubbles: players meeting each other, and persons under the mouse (PERSONS::TryMoods).</summary>
        internal void TryMoods()
        {
            if (Clock.Hour == 9 && Clock.Minute == 0) return;
            var idx = new Person[Player.Count];
            foreach (var p in Persons)
                if (p.Alive && Clan(p.ClanId) is ClanDef k && k.IsPlayer && p.StatePar == 0 && Players[p.State].InHall) idx[p.State] = p;
            for (int c = 0; c < Player.Count; c++)
                for (int d = 0; d < Player.Count; d++)
                {
                    if (c == d || idx[c] == null || idx[d] == null) continue;
                    var dp = idx[c].Position - idx[d].Position;
                    if (Math.Abs(dp.X) >= 80 || Math.Abs(dp.Y) >= 60) continue;
                    if (Players[c].IsStuck > 0) Bubble(idx[c], Mood.Stuck);
                    else if (World.Players[c].IsComputer) Bubble(idx[c], Players[c].RunningToToilet ? Mood.Toilet : SympathyMood(c, d));
                }
            if (Hover == null) return;
            var m = Hover.Value;
            foreach (var p in Persons)
            {
                if (!p.Alive) continue;
                if (Math.Abs(m.X - p.ScreenPos.X) >= 40 || Math.Abs(m.Y - (p.ScreenPos.Y - 40)) >= 80) continue;
                var k = Clan(p.ClanId);
                if (k == null) continue;
                if (k.IsPlayer)
                {
                    int pn = p.State;
                    if (Players[pn].IsStuck > 0) Bubble(p, Mood.Stuck);
                    else if (idx[pn] != null && World.Players[pn].IsComputer)
                    {
                        p.Mood = Players[pn].RunningToToilet ? Mood.Toilet : SympathyMood(pn, World.LocalPlayer);
                        Refresh(p, Mood.CountStart);
                    }
                }
                else if ((k.Type < 30 && k.Group < 30) || k.Group == 240)
                {
                    if (p.Mood != 0) Refresh(p, Mood.CountStart + CosmeticRand().Rand(15));
                }
            }
        }

        /// <summary>One shopper every 64 ticks 09:00-12:00 and 14:00-16:00 while there are &lt; 70 shoppers and &lt; 100 persons.</summary>
        internal void AddNewShoppers()
        {
            int t = Clock.Time;
            if (!((t > 9 * 60000 && t <= 12 * 60000) || (t >= 14 * 60000 && t <= 16 * 60000))) return;
            if (NumShoppers() >= 70 || Clock.Frozen || World.Paused) return;
            int clan = CustomerId(0, 99, World.PersonRandCreate);
            var birth = RandomRune(RuneId.Creation, 0, World.PersonRandCreate);
            if (clan < 0 || birth == null) return;
            if (NumUsed < 100) AddPersonToQueue(clan, birth.Value, Reason.Shopping, 0, 0, 0);
        }
    }

    /// <summary>The hall's time-driven behaviour. Order 300 (step 3 of the per-tick sequence).</summary>
    public sealed class HallSystem : SimSystem
    {
        public const string SystemName = "hall";
        public override string Name => SystemName;
        public override int Order => 300;

        public override void OnTick(World w)
        {
            var h = w.Hall;
            if (h == null) return;
            h.RunPendingSpawnPhase();
            h.WalkPlayers();
            h.UpdateStayLimit();
            h.StepPersons();
            h.RecordSpawnPhase();
        }

        public override void OnNewDay(World w) { }
    }

    /// <summary>Midnight step 9-11 (sim-core.md §2.6): occupancy, clan rotation, pedestrian reseed, queue, floor flags.</summary>
    public sealed class HallNightSystem : SimSystem
    {
        public override string Name => "hall.night";
        public override int Order => 900;
        public override void OnNewDay(World w)
        {
            var h = w.Hall;
            if (h == null) return;
            Array.Clear(h.RoomBusy, 0, h.RoomBusy.Length);
            h.UpdateClansInGame(false);
            w.PersonRandMisc.SetSeed(w.Clock.Date);
            w.PersonRandCreate.SetSeed(w.Clock.Date);
            h.Queue.Clear();
            // Step 11: clear glue/stench/person markers, recompute seats.
            for (int i = 0; i < h.Plates.Length; i++) h.Plates[i] = (byte)(h.Plates[i] & ~(Plate.Occupied | Plate.Special) | (h.Layout.InitialPlates[i] & Plate.Special));
            for (int i = 0; i < h.PlateDir.Length; i++) h.PlateDir[i] = 255;
            Array.Copy(h.Layout.InitialSeats, h.Seats, h.Seats.Length);
        }
    }

    /// <summary>After the midnight sequence: AIRPORT::NewDay (persons cleared, players and walkers respawned).</summary>
    public sealed class HallNewDaySystem : SimSystem
    {
        public override string Name => "hall.new-day";
        public override int Order => 3100;
        public override void OnNewDay(World w)
        {
            var h = w.Hall;
            if (h == null) return;
            h.AnyPersonsInAirport = false;
            h.NewDay();
        }
    }
}
