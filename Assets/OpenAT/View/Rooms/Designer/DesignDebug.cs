using System;
using System.IO;
using OpenAT.Sim;
using OpenAT.Sim.Data;
using OpenAT.View.Boot;
using UnityEngine;

namespace OpenAT.View.Rooms.Designer
{
    /// <summary>
    /// Debug switch for looking at designer planes in the hall: <c>-openat-debug-designplane &lt;file.plane&gt;</c>
    /// turns the human airline's first starting plane into a plane built from that design at new game
    /// (before <c>-openat-debug-autoplan</c> plans it). Changes nothing unless given.
    /// </summary>
    public static class DesignDebug
    {
        const string Flag = "-openat-debug-designplane";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            var file = CommandLine.Value(Flag);
            if (string.IsNullOrEmpty(file)) return;
            World.SetupStepFactories.Add(() => new Step(file));
            Debug.Log("OpenAT debug: designer plane from " + file);
        }

        sealed class Step : ISetupStep
        {
            readonly string _file;
            public Step(string file) { _file = file; }
            public string Name => "debug.designplane";
            public int Order => 945;

            public void Apply(World w, NewGameSettings s)
            {
                PlaneDesign d;
                try { d = PlaneDesign.ReadPln(File.ReadAllBytes(_file)); }
                catch (Exception e) { Debug.LogWarning("OpenAT debug: cannot read " + _file + ": " + e.Message); return; }
                var p = w.Players[s.LocalPlayer];
                if (p.Planes.Count == 0 || !d.IsBuildable(w.DesignCatalog)) return;
                var pl = p.Planes[0];
                var st = d.Stats(w.DesignCatalog);
                pl.TypeId = -1;
                pl.Design = d;
                pl.Model = d.Name;
                pl.Seats = st.Seats; pl.Speed = st.Speed; pl.Range = st.Range;
                Debug.Log("OpenAT debug: " + pl.Name + " is now the design '" + d.Name + "'");
            }
        }
    }
}
