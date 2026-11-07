namespace OpenAT.Sim
{
    public sealed partial class World
    {
        /// <summary>Letters (Sim/Mail): fills the humans' in-trays from <see cref="Fleet.LetterSent"/>.</summary>
        partial void InitMail();

        partial void InitMail() => Mail.MailModule.Register(this);
    }
}
