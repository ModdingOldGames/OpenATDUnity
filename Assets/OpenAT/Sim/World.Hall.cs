namespace OpenAT.Sim
{
    /// <summary>
    /// Airport hall (airport-and-people.md): walk grid, persons, doors. Created by the "hall" setup step
    /// registered through <see cref="global::OpenAT.Sim.Hall.HallModule.Register"/>, or on demand by
    /// <see cref="global::OpenAT.Sim.Hall.HallModule.EnsureHall"/>. Null while the hall content is not
    /// available (e.g. headless tests without an install).
    /// </summary>
    public sealed partial class World
    {
        public global::OpenAT.Sim.Hall.HallState Hall;
    }
}
