namespace OpenAT.Sim.Core
{
    /// <summary>Outcome of executing a command. <see cref="Reason"/> is a stable key, not UI text.</summary>
    public readonly struct CommandResult
    {
        public readonly bool Ok;
        public readonly string Reason;

        CommandResult(bool ok, string reason) { Ok = ok; Reason = reason; }

        public static readonly CommandResult Success = new CommandResult(true, null);
        public static CommandResult Fail(string reason) => new CommandResult(false, reason);
        public override string ToString() => Ok ? "ok" : "fail: " + Reason;
    }

    /// <summary>
    /// A player or AI action. Validation and effect live together so the UI, the AI, tests and
    /// network play all go through the same path.
    /// </summary>
    public interface ICommand<in TWorld>
    {
        /// <summary>Checks the command against the current state without changing anything.</summary>
        CommandResult Validate(TWorld world);

        /// <summary>Applies the command. Only called after a successful <see cref="Validate"/>.</summary>
        void Apply(TWorld world);
    }
}
