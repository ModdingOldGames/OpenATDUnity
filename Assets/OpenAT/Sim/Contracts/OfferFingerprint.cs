namespace OpenAT.Sim.Contracts
{
    /// <summary>
    /// The content of an offer slot (OpenAT planner improvements). A slot number alone does not identify an
    /// offer, because boards refill. The trial keeps this fingerprint and checks it against the board.
    /// </summary>
    public sealed class OfferFingerprint
    {
        public bool Freight;
        public int From, To, Amount, Date, LastDate, Premium, Penalty;

        public static OfferFingerprint Of(World w, OfferBoard board, int slot)
        {
            if (board == OfferBoard.FreightOffice)
            {
                var f = w.FreightOffice.Offers[slot];
                if (f == null || f.Premium < 0) return null;
                return new OfferFingerprint { Freight = true, From = f.From, To = f.To, Amount = f.Tons, Date = f.Date, LastDate = f.LastDate, Premium = f.Premium, Penalty = f.Penalty };
            }
            var b = board == OfferBoard.LastMinute ? w.LastMinute : board == OfferBoard.TravelAgency ? w.TravelAgency : null;
            if (b == null || slot < 0 || slot >= ContractBoard.Slots || b.IsEmpty(slot)) return null;
            var c = b.Offers[slot];
            return new OfferFingerprint { From = c.From, To = c.To, Amount = c.Persons, Date = c.Date, LastDate = c.LastDate, Premium = c.Premium, Penalty = c.Penalty };
        }

        public bool SameAs(OfferFingerprint o) => o != null && o.Freight == Freight && o.From == From && o.To == To && o.Amount == Amount
            && o.Date == Date && o.LastDate == LastDate && o.Premium == Premium && o.Penalty == Penalty;
    }
}
