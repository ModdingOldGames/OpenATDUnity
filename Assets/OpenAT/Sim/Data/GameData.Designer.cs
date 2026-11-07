using System.Collections.Generic;
using OpenAT.Formats;

namespace OpenAT.Sim.Data
{
    public sealed partial class GameData
    {
        /// <summary>Designer part catalogue with the install's builds.csv / relation.csv (planes-and-flights.md §5.2–5.3).</summary>
        public PlaneDesignCatalog DesignCatalog;
        /// <summary>Prebuilt designer planes of the AI by mission (misc/pl45.pln, pl48.pln; data-tables.md §2.4).</summary>
        public readonly Dictionary<int, PlaneDesign> MissionDesigns = new Dictionary<int, PlaneDesign>();

        void LoadDesigner(GameInstall install)
        {
            DesignCatalog = PlaneDesignCatalog.Load(install);
            foreach (int mission in new[] { 45, 48 })
            {
                string f = "misc/pl" + mission + ".pln";
                if (!install.Exists(f)) continue;
                try { MissionDesigns[mission] = PlaneDesign.ReadPln(install.ReadAllBytes(f)); }
                catch (System.Exception e) when (e is System.ArgumentException || e is System.IndexOutOfRangeException) { }
            }
        }
    }
}
