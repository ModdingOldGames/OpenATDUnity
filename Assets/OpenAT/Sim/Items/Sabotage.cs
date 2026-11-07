using System.Collections.Generic;

namespace OpenAT.Sim.Items
{
    /// <summary>The three prank families of the saboteur (items-security-special.md §1.3, sim-core.md §6.7).</summary>
    public enum PrankFamily
    {
        /// <summary>"ArabMode" 1..5: executed on the hour when the target plane is in the air (flat tyre: at departure).</summary>
        Plane = 1,
        /// <summary>"ArabMode2" 1..4: executed when leaving the morning briefing.</summary>
        Person = 2,
        /// <summary>"ArabMode3" 1..6: executed when leaving the morning briefing.</summary>
        Special = 3,
    }

    /// <summary>
    /// A boss's dealings with the saboteur (items-security-special.md §1.1): trust, evidence ("hints")
    /// and the single pending order. <see cref="PlaneTarget"/> is a plane id of the victim (plane
    /// pranks, special 5) or a route table index (special 6).
    /// </summary>
    public sealed class SabotageState
    {
        /// <summary>"ArabTrust" 0..5 (6 only by cheat): gates the offered options.</summary>
        public int Trust;
        /// <summary>"ArabHints": evidence, −3 per night, exposure at ≥ 100.</summary>
        public int Hints;

        /// <summary>Pending plane prank 1..5, or 0.</summary>
        public int PlaneMode;
        public int PlaneVictim = -1;
        /// <summary>Victim's plane id (plane pranks and special 5) or route index (special 6).</summary>
        public int PlaneTarget = -1;
        /// <summary>"ArabActive": the plane was seen on the ground since the order.</summary>
        public bool PlaneArmed;
        /// <summary>Plane prank chosen in the dialog but the plane not yet picked (mode, or 0).</summary>
        public int PlaneSelecting;

        public int PersonMode;
        public int PersonVictim = -1;

        public int SpecialMode;
        public int SpecialVictim = -1;

        /// <summary>The pending order was injected by the "crisis management" mission (anonymous, no hints).</summary>
        public bool PlaneForeign, PersonForeign, SpecialForeign;

        /// <summary>Robot: orders refused by security (pliers trigger, §1.5).</summary>
        public int Annoyances;

        public bool AnyPending => PlaneMode != 0 || PersonMode != 0 || SpecialMode != 0;

        public void ClearPending()
        {
            PlaneMode = PersonMode = SpecialMode = 0;
            PlaneVictim = PersonVictim = SpecialVictim = PlaneTarget = -1;
            PlaneArmed = false;
            PlaneSelecting = 0;
            PlaneForeign = PersonForeign = SpecialForeign = false;
        }
    }

    /// <summary>
    /// One executed prank, listed at the next briefing (the original's "SabotageActs"). <see cref="ActCode"/>
    /// is the boss text code: plane mode 1..5, person 2075..2078, special 2091..2096.
    /// </summary>
    public sealed class SabotageAct
    {
        /// <summary>Perpetrator airline, or <see cref="Foreign"/>.</summary>
        public int Perpetrator;
        public int Victim;
        public PrankFamily Family;
        public int Mode;
        public int ActCode;
        public int Date;
        public const int Foreign = -2;
        public bool IsForeign => Perpetrator == Foreign;

        public static int Code(PrankFamily f, int mode) =>
            f == PrankFamily.Plane ? mode : f == PrankFamily.Person ? 2075 + mode - 1 : 2090 + mode;

        public override string ToString() => $"act {Family}{Mode} {Perpetrator}->{Victim} d{Date}";
    }

    /// <summary>One line of the boss's sabotage report (rooms-and-ui-3b §"Sabotage report").</summary>
    public sealed class SabotageReportLine
    {
        public SabotageAct Act;
        /// <summary>Boss line: 2070/2071/2072 ("no idea who") or 2073 (culprit named).</summary>
        public int CommentId;
        /// <summary>Line that introduces the victim: 2050 + escalation (0..3), or 2055 when the victim was named before.</summary>
        public int IntroId;
        /// <summary>Fine paid by the culprit to the victim (hints × 10 000), 0 when not exposed.</summary>
        public long Fine;
    }

    /// <summary>
    /// Price, trust and evidence tables of the pranks (items-security-special.md §1.3, economy.md §15.7)
    /// and the security bit that blocks / is "needed" for each (§2.2, §2.4). Index = mode (1-based).
    /// </summary>
    public static class SabotageTables
    {
        public static readonly long[] PlanePrice = { 0, 1000, 5000, 10000, 50000, 100000 };
        public static readonly long[] PersonPrice = { 0, 10000, 25000, 50000, 250000 };
        public static readonly long[] SpecialPrice = { 0, 100000, 500000, 1000000, 2000000, 5000000, 8000000 };

        public static readonly int[] PlaneHints = { 0, 2, 4, 10, 20, 100 };
        public static readonly int[] PersonHints = { 0, 8, 0, 25, 40 };
        public static readonly int[] SpecialHints = { 0, 8, 15, 25, 30, 50, 70 };

        /// <summary>Victim's sympathy towards the saboteur: plane pranks −5/−15/−35/−80/−200, coffee −10, bomb −50.</summary>
        public static readonly int[] PlaneSympathy = { 0, -5, -15, -35, -80, -200 };
        public static readonly int[] PersonSympathy = { 0, -10, 0, -50, 0 };

        /// <summary>Number of options per family (the dialog lists 1..min(cap, trust)).</summary>
        public static int Cap(PrankFamily f) => f == PrankFamily.Plane ? 5 : f == PrankFamily.Person ? 4 : 6;

        public static long Price(PrankFamily f, int mode)
        {
            var t = f == PrankFamily.Plane ? PlanePrice : f == PrankFamily.Person ? PersonPrice : SpecialPrice;
            return mode >= 1 && mode < t.Length ? t[mode] : 0;
        }

        public static int Hints(PrankFamily f, int mode)
        {
            var t = f == PrankFamily.Plane ? PlaneHints : f == PrankFamily.Person ? PersonHints : SpecialHints;
            return mode >= 1 && mode < t.Length ? t[mode] : 0;
        }

        /// <summary>Security bit that blocks the prank (−1: nothing blocks it). Mapping quirks kept (§2.2).</summary>
        public static int BlockingBit(PrankFamily f, int mode)
        {
            switch (f)
            {
                case PrankFamily.Plane: return mode == 3 || mode == 4 ? SecurityBits.PlaneExterior : SecurityBits.PlaneInterior;
                case PrankFamily.Person:
                    switch (mode) { case 1: case 3: return SecurityBits.Office; case 2: return SecurityBits.Laptop; case 4: return SecurityBits.Personnel; }
                    break;
                case PrankFamily.Special:
                    switch (mode)
                    {
                        case 1: case 5: return SecurityBits.Gate;     // QUIRK: brochures are blocked by the gate scanner
                        case 2: case 3: case 6: return SecurityBits.Administration; // route theft by the administration flag
                        case 4: return SecurityBits.Bank;
                    }
                    break;
            }
            return -1;
        }

        /// <summary>Saboteur's refusal line when the victim is protected (Sabo 2090..2098).</summary>
        public static int RefusalText(PrankFamily f, int mode)
        {
            switch (BlockingBit(f, mode))
            {
                case SecurityBits.Office: return 2090;
                case SecurityBits.Laptop: return 2091;
                case SecurityBits.Personnel: return 2092;
                case SecurityBits.Bank: return 2093;
                case SecurityBits.Administration: return 2095;
                case SecurityBits.PlaneInterior: return 2096;
                case SecurityBits.PlaneExterior: return 2097;
                case SecurityBits.Gate: return 2098;
            }
            return 0;
        }

        /// <summary>Newspaper picture of a fired plane prank (headline <c>Misc 2000 + mode</c>).</summary>
        public static readonly string[] PlanePicture = { null, "SALZ", "SKELETT", "REIFEN", "FEUER", "SUPERMAN" };
    }

    /// <summary>Saboteur dialog text ids (group <c>Sabo</c>) a refused order maps to.</summary>
    public static class SaboText
    {
        public const int NoTrust = 1021, VictimOut = 1045, AlreadyPending = 1100, NoLaptop = 1300, CannotAfford = 6000;
    }

    /// <summary>Per-player record of yesterday's report, for views that replay the boss's lines.</summary>
    public sealed class SabotageReport
    {
        public int Date = -1;
        public readonly List<SabotageReportLine> Lines = new List<SabotageReportLine>();
        /// <summary>A culprit was exposed: the briefing cannot be skipped (§4.3).</summary>
        public bool HasExposure { get { foreach (var l in Lines) if (l.Fine > 0) return true; return false; } }
    }
}
