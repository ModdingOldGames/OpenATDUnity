using OpenAT.Sim.Core;

namespace OpenAT.Sim.AI
{
    /// <summary>Job family of a sabotage order (ai-robots.md §5.2 "Sabotage"; the three pending-job fields).</summary>
    public enum SabotageFamily
    {
        /// <summary>Plane job (flight-time sabotage, severity 1..4) against a plane of the target.</summary>
        Plane = 0,
        /// <summary>Office / personal job (overnight sabotage, severity 1..4).</summary>
        Office = 1,
        /// <summary>Special job ("next day" sabotage, severity 1..5; 5 needs a target plane).</summary>
        Special = 2,
    }

    /// <summary>
    /// What the AI needs from the items / sabotage / security domain (docs/spec/integration.md "AI needs
    /// from Items"). The AI only *decides* (who, which family, which severity, whether the target is
    /// protected); the shared mechanics (job fields, effects, hints, the bolt cutter, items in the
    /// inventory, laptop/phone) belong to that domain, which replaces <see cref="NoRivalActions"/> with
    /// <c>Rules.Set&lt;IRivalActions&gt;(...)</c> in its <c>InitItems()</c>.
    /// </summary>
    public interface IRivalActions : IRule
    {
        // ---- sabotage (ai-robots.md §5.2 "Sabotage")
        /// <summary>Suspicion level of the saboteur towards this airline ("ArabHints", 0..100).</summary>
        int SuspicionLevel(World w, Player p);
        /// <summary>A sabotage job of any family is already pending for this airline.</summary>
        bool HasPendingSabotage(World w, Player p);
        /// <summary>Trust between this airline and the saboteur (0..5); the robot raises it by one per go decision.</summary>
        int SaboteurTrust(World w, Player p);
        void SetSaboteurTrust(World w, Player p, int trust);
        /// <summary>
        /// Writes a sabotage job into the attacker's pending-job field of that family, exactly as a human
        /// order in the saboteur's room would (robots pay nothing, §9.2). <paramref name="planeId"/> is the
        /// target plane for plane jobs and special severity 5, else 0. Returns false if refused.
        /// </summary>
        bool PlaceSabotage(World w, Player attacker, Player target, SabotageFamily family, int severity, int planeId);

        // ---- security office (§5.2 "Security", "Security-2")
        /// <summary>The security office exists on this airport and its power is on.</summary>
        bool SecurityOfficeOpen(World w);
        /// <summary>The bolt cutter can still be picked up today.</summary>
        bool BoltCutterAvailable(World w);
        /// <summary>The robot takes the bolt cutter into its inventory (free); false if it could not (inventory full).</summary>
        bool TakeBoltCutter(World w, Player p);
        /// <summary>If the robot holds the bolt cutter: drop it, cut the security office power for 3 days and clear every airline's protections.</summary>
        void CutSecurityPower(World w, Player p);

        // ---- items (§5.2 "Duty free", planner filters, §9.2)
        /// <summary>Laptop quality 0 (none) .. 4.</summary>
        int LaptopQuality(World w, Player p);
        /// <summary>Has a laptop without a virus.</summary>
        bool LaptopWorking(World w, Player p);
        /// <summary>Somebody already bought the laptop today (only one per day).</summary>
        bool LaptopSoldToday(World w);
        /// <summary>Free laptop for a robot: quality +1, the item if not owned, marks the laptop as sold today.</summary>
        void GiveLaptop(World w, Player p);
        bool HasMobilePhone(World w, Player p);
        /// <summary>Free mobile phone for a robot.</summary>
        void GiveMobilePhone(World w, Player p);
        /// <summary>The airline's office is destroyed (bomb).</summary>
        bool OfficeDestroyed(World w, Player p);

        // ---- human contact (§5.1)
        /// <summary>The human can be phoned now (mobile phone, in a room, no dialog, phone not sabotaged, 09:00–17:59).</summary>
        bool CanPhone(World w, Player human);
    }

    /// <summary>Default when no items domain is present: nobody is suspicious, sabotage is never placed, no items.</summary>
    public sealed class NoRivalActions : IRivalActions
    {
        public int SuspicionLevel(World w, Player p) => 0;
        public bool HasPendingSabotage(World w, Player p) => false;
        public int SaboteurTrust(World w, Player p) => 0;
        public void SetSaboteurTrust(World w, Player p, int trust) { }
        public bool PlaceSabotage(World w, Player attacker, Player target, SabotageFamily family, int severity, int planeId) => false;
        public bool SecurityOfficeOpen(World w) => true;
        public bool BoltCutterAvailable(World w) => false;
        public bool TakeBoltCutter(World w, Player p) => false;
        public void CutSecurityPower(World w, Player p) { }
        public int LaptopQuality(World w, Player p) => 0;
        public bool LaptopWorking(World w, Player p) => false;
        public bool LaptopSoldToday(World w) => false;
        public void GiveLaptop(World w, Player p) { }
        public bool HasMobilePhone(World w, Player p) => false;
        public void GiveMobilePhone(World w, Player p) { }
        public bool OfficeDestroyed(World w, Player p) => false;
        public bool CanPhone(World w, Player human) => false;
    }

    /// <summary>A robot decided to phone a human (ai-robots.md §5.1). The dialog itself belongs to the rooms domain.</summary>
    public sealed class RobotPhoneCall : ISimEvent
    {
        public readonly Player Robot, Human;
        public RobotPhoneCall(Player robot, Player human) { Robot = robot; Human = human; }
    }

    /// <summary>A robot's sabotage decision (placed or blocked by the target's security), for logs and newspapers.</summary>
    public sealed class RobotSabotageDecided : ISimEvent
    {
        public Player Robot, Target;
        public SabotageFamily Family;
        public int Severity;
        public bool Blocked, Placed;
    }
}
