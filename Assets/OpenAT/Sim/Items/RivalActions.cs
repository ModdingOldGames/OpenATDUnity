using OpenAT.Sim.AI;

namespace OpenAT.Sim.Items
{
    /// <summary>
    /// The items domain's side of the computer players (docs/spec/integration.md "AI needs from Items",
    /// items-security-special.md §1.5, §2.4, §2.5, §3.2): sabotage job fields, the pliers ("bolt cutter"),
    /// laptop and phone.
    /// </summary>
    public sealed class ItemsRivalActions : IRivalActions
    {
        public int SuspicionLevel(World w, Player p) => p.Sabotage.Hints;
        public bool HasPendingSabotage(World w, Player p) => p.Sabotage.AnyPending;
        public int SaboteurTrust(World w, Player p) => p.Sabotage.Trust;
        public void SetSaboteurTrust(World w, Player p, int trust) => p.Sabotage.Trust = trust < 0 ? 0 : trust;

        public bool PlaceSabotage(World w, Player attacker, Player target, SabotageFamily family, int severity, int planeId)
        {
            if (attacker == null || target == null) return false;
            var f = family == SabotageFamily.Plane ? PrankFamily.Plane : family == SabotageFamily.Office ? PrankFamily.Person : PrankFamily.Special;
            bool needsPlane = f == PrankFamily.Plane || (f == PrankFamily.Special && severity == 5);
            if (needsPlane && target.Planes.Find(planeId) == null) return false;
            return SabotageEngine.RobotOrder(w, attacker, target.Index, f, severity, needsPlane ? planeId : -1);
        }

        public bool SecurityOfficeOpen(World w) => w.Items.SecurityOutageDays == 0;
        public bool BoltCutterAvailable(World w) => w.Items.Pliers;

        public bool TakeBoltCutter(World w, Player p)
        {
            if (!w.Items.Pliers || !ItemEngine.Give(w, p, ItemId.Pliers, "found")) return false;
            w.Items.Pliers = false;
            return true;
        }

        public void CutSecurityPower(World w, Player p)
        {
            if (!ItemEngine.Take(w, p, ItemId.Pliers, "use")) return;
            ItemEngine.PowerOutage(w, p);
        }

        public int LaptopQuality(World w, Player p) => p.LaptopQuality;
        public bool LaptopWorking(World w, Player p) => p.HasItem(ItemId.Laptop) && p.LaptopVirus == 0;
        public bool LaptopSoldToday(World w) => w.Items.LaptopSoldTo != -1;
        public void GiveLaptop(World w, Player p)
        {
            if (p.LaptopQuality >= 4 && p.HasItem(ItemId.Laptop)) return;
            ItemEngine.BuyLaptop(w, p, free: true);
        }

        public bool HasMobilePhone(World w, Player p) => p.HasItem(ItemId.Phone);
        public void GiveMobilePhone(World w, Player p) { if (!p.HasItem(ItemId.Phone)) ItemEngine.Give(w, p, ItemId.Phone, "shop"); }
        public bool OfficeDestroyed(World w, Player p) => p.OfficeState == 2;

        public bool CanPhone(World w, Player human)
        {
            if (human.IsOut || !human.HasItem(ItemId.Phone) || human.PhonesDown) return false;
            int t = w.Clock.Time;
            if (t < Core.GameClock.DayStart || t >= 18 * 60000) return false;
            var h = w.Hall;
            if (h != null)
            {
                var hp = h.Players[human.Index];
                if (hp.IsStuck > 0 || hp.RunningToToilet) return false;
            }
            return true;
        }
    }
}
