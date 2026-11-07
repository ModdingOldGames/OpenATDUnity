using OpenAT.Sim.Core;

namespace OpenAT.Sim
{
    /// <summary>
    /// Switches a boolean <see cref="GameOptions"/> field of a running game (the options screen's OpenAT extras page).
    /// Goes through the command pipeline like every other change, so replays and the save see it.
    /// </summary>
    public sealed class SetOptionCommand : ICommand<World>
    {
        public readonly string Name;
        public readonly bool Value;

        public SetOptionCommand(string name, bool value) { Name = name; Value = value; }

        public CommandResult Validate(World w)
        {
            var f = Name == null ? null : typeof(GameOptions).GetField(Name);
            if (f == null || f.FieldType != typeof(bool)) return CommandResult.Fail("option.unknown");
            return CommandResult.Success;
        }

        public void Apply(World w) => typeof(GameOptions).GetField(Name).SetValue(w.Options, Value);
    }
}
