namespace OpenAT.Sim
{
    public sealed partial class Player
    {
        /// <summary>
        /// A computer airline got bored of the human on the phone today (rooms-and-ui-3 §8 timeouts, "BoredOfPlayer"):
        /// further calls to it get no answer until the next day. Reset by the per-airline new day.
        /// </summary>
        public bool BoredOfPlayer;
    }
}
