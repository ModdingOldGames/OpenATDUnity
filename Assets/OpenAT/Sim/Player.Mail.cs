using OpenAT.Sim.Mail;

namespace OpenAT.Sim
{
    public sealed partial class Player
    {
        /// <summary>Letters and faxes in the office in-tray (Sim/Mail), newest first.</summary>
        public readonly Mailbox Mail = new Mailbox();
    }
}
