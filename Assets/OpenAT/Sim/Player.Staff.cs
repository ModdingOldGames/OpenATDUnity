using System.Collections.Generic;
using OpenAT.Sim.Core;
using OpenAT.Sim.Finance;
using OpenAT.Sim.Staff;

namespace OpenAT.Sim
{
    /// <summary>Crew assigned to one plane by <see cref="Player.MapWorkers"/> (economy.md §9.4).</summary>
    public struct CrewInfo
    {
        /// <summary>Counted pilots / attendants as the original keeps them on the plane ("AnzPiloten"/"AnzBegleiter").</summary>
        public int Pilots, Attendants;
        /// <summary>Crew quality = average talent of the assigned crew, 60 if none (original <c>PersonalQuality</c>).</summary>
        public int Quality;
    }

    /// <summary>
    /// Staff of an airline (economy.md §9): counts used by other domains, crew mapping, salaries,
    /// advisor quality, strikes. Workers themselves live in <see cref="World.Workers"/>.
    /// </summary>
    public sealed partial class Player
    {
        /// <summary>
        /// Employed pilots. Humans: workers of type pilot; computers: the required pilots of their
        /// planes (virtual staff, economy.md §9.9).
        /// </summary>
        public int PilotCount => IsComputer ? RequiredCrew(true) : CountWorkers(WorkerType.Pilot);
        /// <summary>Employed flight attendants (computers: required attendants of their planes).</summary>
        public int AttendantCount => IsComputer ? RequiredCrew(false) : CountWorkers(WorkerType.Attendant);

        /// <summary>Surplus (+) / shortage (−) of pilots and attendants after the last mapping (original xPiloten/xBegleiter).</summary>
        public int PilotSurplus, AttendantSurplus;

        readonly Dictionary<int, CrewInfo> _crew = new Dictionary<int, CrewInfo>();

        // ---- strike state (economy.md §9.7, sim-core.md §6.1)
        /// <summary>Remaining strike hours; &gt; 0 = strike running (flights are delayed).</summary>
        public int StrikeHours;
        /// <summary>A strike will start at the next opportunity (also set by the "provoke strike" sabotage).</summary>
        public bool StrikePlanned;
        /// <summary>Game minutes until a negotiated end.</summary>
        public int StrikeEndCountdown;
        /// <summary>1 threatened, 2 salaries raised, 3 waited out / drunk bribed; 0 none.</summary>
        public int StrikeEndType;
        /// <summary>The player has been told about the current strike start/end.</summary>
        public bool StrikeNotified;
        public int DaysWithoutStrike = 20;

        public bool StrikeRunning => StrikeHours > 0;

        IEnumerable<Worker> StaffMembers()
        {
            if (World == null) yield break;
            foreach (var wk in World.Workers.Workers) if (wk.Employer == Index) yield return wk;
        }

        int CountWorkers(int type)
        {
            int n = 0;
            foreach (var wk in StaffMembers()) if (wk.Type == type) n++;
            return n;
        }

        int RequiredCrew(bool pilots)
        {
            if (World == null) return 0;
            int n = 0;
            foreach (var pl in World.OperationsBridge.Planes(World, this)) n += pilots ? pl.RequiredPilots : pl.RequiredAttendants;
            return n;
        }

        /// <summary>Number of employed workers (all types).</summary>
        public int EmployeeCount { get { int n = 0; foreach (var _ in StaffMembers()) n++; return n; } }

        /// <summary>
        /// Advisor quality = highest talent among the employed advisors of that type, 0 if none
        /// (economy.md §9.8, original <c>HasBerater</c>).
        /// </summary>
        public int AdvisorQuality(AdvisorType type)
        {
            int max = 0;
            foreach (var wk in StaffMembers()) if (wk.Type == (int)type && wk.Talent > max) max = wk.Talent;
            return max;
        }

        public bool HasAdvisor(AdvisorType type) => AdvisorQuality(type) > 0;

        /// <summary>Walk speed = 3 + Σ (talent − 30)/30 over fitness trainers, clamped 1..5 (economy.md §9.8).</summary>
        public int FitnessWalkSpeed()
        {
            int s = 3;
            foreach (var wk in StaffMembers()) if (wk.Type == (int)AdvisorType.Fitness) s += (wk.Talent - 30) / 30;
            return s < 1 ? 1 : s > 5 ? 5 : s;
        }

        /// <summary>
        /// Sets the hall figure's step size from the fitness trainers (original <c>UpdateWalkSpeed</c>):
        /// after every crew re-mapping (hire, fire, quit) and every night. The figure moves 2W px
        /// horizontally and W px vertically per step, running does two steps, so walking and running both
        /// speed up. Computer airlines have no trainers and stay at 3; remote humans keep their own value.
        /// </summary>
        public void UpdateWalkSpeed()
        {
            if (Owner == Owner.RemoteHuman) return;
            var hall = World?.Hall;
            if (hall == null) return;
            hall.Players[Index].WalkSpeed = FitnessWalkSpeed();
        }

        // ------------------------------------------------------------------ crew per plane (API for flights)

        /// <summary>Crew counts and quality of a plane after the last <see cref="MapWorkers"/>.</summary>
        public CrewInfo Crew(int planeId) => _crew.TryGetValue(planeId, out var c) ? c : new CrewInfo { Quality = 60 };
        /// <summary>Crew quality of a plane (average talent, 60 if none).</summary>
        public int CrewQuality(int planeId) => Crew(planeId).Quality;

        /// <summary>Daily salary share of a plane's crew (Σ salary/30), for the plane's P&amp;L (economy.md §9.5).</summary>
        public long CrewSalaryPerDay(int planeId)
        {
            long s = 0;
            foreach (var wk in StaffMembers()) if (wk.PlaneId == planeId) s += wk.Salary / 30;
            return s;
        }

        /// <summary>
        /// Assigns pilots and attendants to planes (economy.md §9.4, original <c>MapWorkers</c>),
        /// recounts the surplus/shortage and recomputes crew quality. Two bugs of the original are fixed
        /// by default (<see cref="GameOptions.FixCrewMapping"/>, off = original): a spare attendant takes
        /// the first free luxury slot only (the original counts her on every plane with a free slot and
        /// keeps the last one), and the second pass that moves luxury attendants to planes short of base
        /// crew runs (in the original its "movable" count compares a value with itself and is always 0).
        /// </summary>
        public void MapWorkers(bool advice)
        {
            if (World == null) return;
            var planes = World.OperationsBridge.Planes(World, this);
            var pool = World.Workers.Workers;
            int lastP = PilotSurplus, lastA = AttendantSurplus;
            int xP = 0, xA = 0;
            int n = planes.Count;
            var pil = new int[n];
            var att = new int[n];
            var index = new Dictionary<int, int>();
            for (int i = 0; i < n; i++) index[planes[i].Id] = i;

            foreach (var wk in pool)
            {
                if (wk.Employer != Index || wk.Type < WorkerType.Pilot) continue;
                if (wk.PlaneId == -1 || !index.ContainsKey(wk.PlaneId))
                {
                    wk.PlaneId = -1;
                    if (wk.IsPilot) xP++;
                    if (wk.IsAttendant) xA++;
                }
                else
                {
                    int d = index[wk.PlaneId];
                    if (wk.IsPilot) pil[d]++;
                    if (wk.IsAttendant) att[d]++;
                }
            }

            // More attendants than the plane's maximum? Free them.
            for (int c = 0; c < n; c++)
                while (att[c] > planes[c].MaxAttendants)
                {
                    bool freed = false;
                    foreach (var wk in pool)
                        if (wk.Employer == Index && wk.IsAttendant && wk.PlaneId == planes[c].Id)
                        { wk.PlaneId = -1; att[c]--; freed = true; break; }
                    if (!freed) break;
                }

            bool fix = World.Options.FixCrewMapping;
            MapFreeCrew(pool, planes, pil, att, ref xP, ref xA, fix);
            // Second pass: planes still short of base attendants while others carry luxury ones? Free
            // luxury attendants and map again. Each round fills at least one base slot (the bound is a guard).
            for (int round = 0; fix && round < 1000; round++)
            {
                int needed = 0, movable = 0;
                for (int d = 0; d < n; d++)
                {
                    needed += System.Math.Max(planes[d].RequiredAttendants - att[d], 0);
                    movable += System.Math.Max(att[d] - planes[d].RequiredAttendants, 0);
                }
                if (needed <= 0 || movable <= 0) break;
                foreach (var wk in pool)
                {
                    if (wk.Employer != Index || !wk.IsAttendant || wk.PlaneId == -1 || !index.TryGetValue(wk.PlaneId, out int d)) continue;
                    if (att[d] <= planes[d].RequiredAttendants) continue;
                    att[d]--;
                    wk.PlaneId = -1;
                    xA++;
                    if (--needed <= 0 || --movable <= 0) break;
                }
                MapFreeCrew(pool, planes, pil, att, ref xP, ref xA, fix);
            }

            for (int d = 0; d < n; d++)
            {
                if (planes[d].RequiredPilots > pil[d]) xP -= planes[d].RequiredPilots - pil[d];
                if (planes[d].MaxAttendants > att[d]) xA -= planes[d].MaxAttendants - att[d];
            }
            PilotSurplus = xP;
            AttendantSurplus = xA;
            UpdateWalkSpeed();

            if (advice && HasAdvisor(AdvisorType.Personnel))
            {
                int adv = (int)AdvisorType.Personnel;
                if (lastP != xP)
                {
                    if (xP > 0) World.Events.Publish(new AdvisorMessage(this, adv, 1200, xP));
                    if (xP == 0) World.Events.Publish(new AdvisorMessage(this, adv, 1201));
                    if (xP == -1) World.Events.Publish(new AdvisorMessage(this, adv, 1202));
                    if (xP < -1) World.Events.Publish(new AdvisorMessage(this, adv, 1203, -xP));
                }
                else if (lastA != xA)
                {
                    if (xA > 0) World.Events.Publish(new AdvisorMessage(this, adv, 1250, xA));
                    if (xA == 0) World.Events.Publish(new AdvisorMessage(this, adv, 1251));
                    if (xA == -1) World.Events.Publish(new AdvisorMessage(this, adv, 1252));
                    if (xA < -1) World.Events.Publish(new AdvisorMessage(this, adv, 1253, -xA));
                }
            }

            _crew.Clear();
            for (int d = 0; d < n; d++)
            {
                int sum = 0, cnt = 0;
                foreach (var wk in pool)
                    if (wk.Employer == Index && wk.PlaneId == planes[d].Id) { sum += wk.Talent; cnt++; }
                _crew[planes[d].Id] = new CrewInfo { Pilots = pil[d], Attendants = att[d], Quality = cnt > 0 ? sum / cnt : 60 };
            }
        }

        /// <summary>
        /// One mapping pass of <see cref="MapWorkers"/>: every unassigned pilot/attendant goes to the plane
        /// with the smallest remaining shortfall of its kind; an attendant finding none takes a free luxury
        /// slot (original: counted on every plane with a free slot, see <see cref="GameOptions.FixCrewMapping"/>).
        /// </summary>
        void MapFreeCrew(List<Worker> pool, IReadOnlyList<PlaneSnapshot> planes, int[] pil, int[] att, ref int xP, ref int xA, bool fix)
        {
            int n = planes.Count;
            foreach (var wk in pool)
            {
                if (wk.Employer != Index || (!wk.IsPilot && !wk.IsAttendant) || wk.PlaneId != -1) continue;
                int minNeeded = 999, best = -1;
                for (int d = 0; d < n; d++)
                {
                    var pl = planes[d];
                    if ((wk.IsPilot && pil[d] < pl.RequiredPilots) || (wk.IsAttendant && att[d] < pl.RequiredAttendants))
                    {
                        int need = wk.IsPilot ? pl.RequiredPilots - pil[d] : pl.RequiredAttendants - att[d];
                        if (need < minNeeded) { minNeeded = need; best = d; }
                    }
                }
                if (best != -1)
                {
                    wk.PlaneId = planes[best].Id;
                    if (wk.IsPilot) { pil[best]++; xP--; }
                    else { att[best]++; xA--; }
                }
                else if (wk.IsAttendant)
                {
                    // Original: no break, every plane with a free luxury slot counts this attendant.
                    for (int d = 0; d < n; d++)
                        if (att[d] < planes[d].MaxAttendants)
                        {
                            wk.PlaneId = planes[d].Id; att[d]++; xA--;
                            if (fix) break;
                        }
                }
            }
        }

        /// <summary>
        /// After a manual reassignment (original <c>MapWorkerOverflow</c>): recount the crews, free pilots
        /// above the required count and attendants above the maximum (secretary note <c>Advi 1260</c>),
        /// recompute crew quality.
        /// </summary>
        public void MapWorkerOverflow(bool advice)
        {
            if (World == null) return;
            var planes = World.OperationsBridge.Planes(World, this);
            var pool = World.Workers.Workers;
            int n = planes.Count;
            var pil = new int[n];
            var att = new int[n];
            var index = new Dictionary<int, int>();
            for (int i = 0; i < n; i++) index[planes[i].Id] = i;
            foreach (var wk in pool)
                if (wk.Employer == Index && wk.Type >= WorkerType.Pilot && wk.PlaneId != -1 && index.TryGetValue(wk.PlaneId, out int d))
                {
                    if (wk.IsPilot) pil[d]++;
                    if (wk.IsAttendant) att[d]++;
                }
            bool needAdvice = false;
            for (int d = 0; d < n; d++)
            {
                while (planes[d].RequiredPilots < pil[d] && Free(pool, planes[d].Id, true)) { pil[d]--; needAdvice = true; }
                while (planes[d].MaxAttendants < att[d] && Free(pool, planes[d].Id, false)) { att[d]--; needAdvice = true; }
            }
            _crew.Clear();
            for (int d = 0; d < n; d++)
            {
                int sum = 0, cnt = 0;
                foreach (var wk in pool)
                    if (wk.Employer == Index && wk.PlaneId == planes[d].Id) { sum += wk.Talent; cnt++; }
                _crew[planes[d].Id] = new CrewInfo { Pilots = pil[d], Attendants = att[d], Quality = cnt > 0 ? sum / cnt : 60 };
            }
            if (needAdvice && advice) World.Events.Publish(new AdvisorMessage(this, AdvisorMessage.Secretary, 1260));
        }

        bool Free(List<Worker> pool, int planeId, bool pilot)
        {
            foreach (var wk in pool)
                if (wk.Employer == Index && (pilot ? wk.IsPilot : wk.IsAttendant) && wk.PlaneId == planeId) { wk.PlaneId = -1; return true; }
            return false;
        }

        /// <summary>
        /// Daily salaries, humans only: Σ salary / 30 (reason 2070; economy.md §9.5), and each plane's
        /// daily balance is charged salary/30 of every crew member assigned to it.
        /// </summary>
        public void BookSalaries()
        {
            if (Owner != Owner.Human) return;
            long sum = 0;
            foreach (var wk in StaffMembers()) sum += wk.Salary;
            foreach (var pl in Planes.Items) pl.Balances[0] -= CrewSalaryPerDay(pl.Id);
            if (sum != 0) Book(-sum / 30, MoneyReason.Salaries);
        }

        /// <summary>
        /// The personnel advisor's staffing remark (original <c>UpdatePersonalberater</c>; on entering the
        /// personnel office with tolerance 3, after buying or selling planes with tolerance 1): spare pilots
        /// above the tolerance (<c>Advi 1100</c>), else spare attendants above it (1101), else both short
        /// (1104), pilots short (1102), attendants short (1103). A comment; null without an advisor or remark.
        /// </summary>
        public AdvisorMessage PersonnelAdvisorRemark(int tolerance)
        {
            if (!HasAdvisor(AdvisorType.Personnel)) return null;
            int adv = (int)AdvisorType.Personnel, xP = PilotSurplus, xA = AttendantSurplus;
            AdvisorMessage m = null;
            if (xP > tolerance) m = new AdvisorMessage(this, adv, 1100, xP);
            else if (xA > tolerance) m = new AdvisorMessage(this, adv, 1101, xA);
            else if (xP < 0 && xA < 0) m = new AdvisorMessage(this, adv, 1104, -xP, -xA);
            else if (xP < 0) m = new AdvisorMessage(this, adv, 1102, -xP);
            else if (xA < 0) m = new AdvisorMessage(this, adv, 1103, -xA);
            if (m != null) m.Comment = true;
            return m;
        }

        /// <summary>Publishes <see cref="PersonnelAdvisorRemark"/> if there is one.</summary>
        public void UpdatePersonnelAdvisor(int tolerance)
        {
            var m = PersonnelAdvisorRemark(tolerance);
            if (m != null) World?.Events.Publish(m);
        }

        /// <summary>
        /// Raise (+10 %, cap 1 000 000, +20 happiness if changed) or cut (−10 %, −25 happiness, quits
        /// below −100) of one worker (economy.md §9.6, original <c>Gehaltsaenderung</c>).
        /// </summary>
        public void ChangeSalary(Worker wk, bool raise)
        {
            if (raise)
            {
                int old = wk.Salary;
                wk.Salary += wk.Salary / 100 * 10;
                if (wk.Salary > 1000000) wk.Salary = 1000000;
                if (wk.Salary != old) wk.Happiness += 20;
            }
            else
            {
                wk.Salary -= wk.Salary / 100 * 10;
                wk.Happiness -= 25;
                if (wk.Happiness < -100)
                {
                    if (Owner == Owner.Human && World != null) WorkerPool.Quit(World, this, wk);
                    wk.Employer = WorkerType.Reserve;
                    wk.Salary = wk.OriginalSalary;
                    wk.Happiness = 100;
                    MapWorkers(false);
                }
            }
        }

        /// <summary>Raise or cut for all employees in pool order (original <c>CWorkers::Gehaltsaenderung</c>).</summary>
        public void ChangeAllSalaries(bool raise)
        {
            if (World == null) return;
            foreach (var wk in World.Workers.Workers.ToArray())
                if (wk.Employer == Index) ChangeSalary(wk, raise);
        }

        /// <summary>
        /// "Raise all" morale pump (economy.md §9.6): +10 to everyone (clamped) while the strike mood
        /// of the local player is below 20. Bounded to 1000 rounds (Q-7).
        /// </summary>
        public void PumpMorale()
        {
            if (World == null) return;
            var pool = World.Workers;
            int local = World.LocalPlayer;
            for (int i = 0; i < 1000 && pool.Mood(local) < 20; i++) pool.AddHappiness(Index, 10);
        }

        /// <summary>
        /// Personnel-advisor comment on an applicant (economy.md §9.3); returns the <c>Advi</c>/personnel
        /// text id, or 0 without a personnel advisor.
        /// </summary>
        public int ApplicantComment(int workerIndex)
        {
            int q = AdvisorQuality(AdvisorType.Personnel);
            if (q == 0 || World == null) return 0;
            var wk = World.Workers[workerIndex];
            if (wk.Employer == WorkerType.Reserve) return wk.Sex == 1 ? 1005 : 1004;
            if (wk.IsAdvisor && HasAdvisor((AdvisorType)wk.Type))
            {
                if (wk.Type == (int)AdvisorType.Personnel) return 1272;
                return wk.Talent > AdvisorQuality((AdvisorType)wk.Type) ? 1271 : 1270;
            }
            if ((long)workerIndex * 7777 % 100 > q) return 1000;
            if (wk.Talent <= 25) return 1006;
            if (wk.Talent <= 45) return 1007;
            int r = World.Workers.QualityRatio(workerIndex);
            if (r < -5) return 1001;
            if (r > 5) return wk.Talent >= 90 ? 1008 : 1003;
            return 1002;
        }
    }
}
