namespace OpenAT.Sim
{
    public sealed partial class World
    {
        /// <summary>Talks between airlines (Sim/Rivals): the nightly reset of the "bored of the player" flag.</summary>
        partial void InitRivals();

        partial void InitRivals() => Systems.Add(new OpenAT.Sim.Rivals.RivalsNewDaySystem());
    }
}
