using OpenAT.Sim;
using OpenAT.Sim.Items;
using OpenAT.Sim.Rivals;
using OpenAT.View.UI.Dialog;
using UnityEngine;

namespace OpenAT.View.Rooms.Rivals
{
    /// <summary>
    /// Talking to a computer airline (<c>TALKER_COMPETITOR</c>, group <c>Play</c>; rooms-and-ui-3b §19,
    /// rooms-and-ui-3 §8). <see cref="DialogSession.Par1"/> = the partner airline, <see cref="DialogSession.Par2"/>
    /// = turn parity: it toggles on every click; 1 after the toggle = our turn (a choice list or our single
    /// line), 0 = the partner's turn (its line is computed here from its sympathy, money and cooperation).
    /// Par2 = 1 at the start means the partner leads (it called us): we answer the phone first.
    /// Effects (sympathy, cooperation) go through <see cref="RivalTalkCommand"/>. Face to face (medium air) while
    /// carrying chocolates the main list has the gift line (2010..2013, <see cref="OfferChocolatesCommand"/>).
    /// Network partners and the chat are not offered (computer airlines only).
    /// </summary>
    [DialogScript(Talkers.Competitor)]
    public sealed class RivalDialog : IDialogScript
    {
        /// <summary>Ids of the main list (offset +0 small talk, +1 cooperation, last = bye), computer partner, no pralines.</summary>
        const int MainFirst = 2000, MainLast = 2002;
        /// <summary>The same with the chocolates line at +2 (in person, carrying a box).</summary>
        const int GiftFirst = 2010, GiftLast = 2013;

        static bool HasChocolates(DialogSession d) =>
            d.Medium == DialogMedium.Air && d.World != null && (d.World.Local.HasItem(ItemId.Chocolates) || d.World.Local.HasItem(ItemId.SpikedChocolates));

        static Player Partner(DialogSession d) => d.World?.Players[Mathf.Clamp(d.Par1, 0, Player.Count - 1)];
        static int Sympathy(DialogSession d) => Partner(d)?.Sympathy[d.World.LocalPlayer] ?? 0;
        static bool Cooperating(DialogSession d) => Partner(d)?.Cooperates[d.World.LocalPlayer] ?? false;

        static void Effect(DialogSession d, int sympathy, bool? cooperation = null)
        {
            var w = d.World;
            if (w == null) return;
            var r = w.Execute(new RivalTalkCommand(d.Par1, w.LocalPlayer, sympathy, cooperation));
            if (!r.Ok) Debug.Log("OpenAT rival talk: " + r);
        }

        static void PartnerSays(DialogSession d, int id, params object[] args)
        {
            d.Say(id, args);
            (d.Stage as RivalCallScreen)?.PartnerTalks(id);
        }

        public void Open(DialogSession d)
        {
            if (d.Par2 < 0) d.Par2 = 0;
            int opening = d.Medium == DialogMedium.Phone ? 1001 : 1000;
            string airline = Partner(d)?.Airline ?? "";
            // par2 = 0: the partner opens; par2 = 1: we open with a single line.
            if (d.Par2 == 0) PartnerSays(d, opening, airline);
            else d.PlayerSays(opening, d.World?.Local.Airline ?? "");
        }

        /// <summary>The main list; the small-talk entry only on the first offer straight after 1600.</summary>
        static void MainList(DialogSession d, bool direct)
        {
            if (HasChocolates(d)) d.Choices(direct ? GiftFirst : GiftFirst + 1, GiftLast);
            else d.Choices(direct ? MainFirst : MainFirst + 1, MainLast);
        }

        /// <summary>The computer opens a topic: cooperation talk when it called about it, else small talk or cooperation.</summary>
        static void PartnerTopic(DialogSession d)
        {
            var screen = d.Stage as RivalCallScreen;
            bool aboutCoop = screen != null && screen.TopicPending;
            if (screen != null) screen.TopicPending = false;
            PartnerSays(d, aboutCoop || Random.value < 0.5f ? 2001 : 2000);
        }

        public void Click(DialogSession d, int id)
        {
            d.Par2 ^= 1;
            bool ours = d.Par2 == 1;           // our turn now: the partner said the clicked line
            var w = d.World;
            var partner = Partner(d);
            if (w == null || partner == null) { d.End(); return; }
            int sym = Sympathy(d);
            bool coop = Cooperating(d);

            switch (id)
            {
                case 1000:
                    if (ours) d.PlayerSays(1200, w.Local.Airline); else PartnerSays(d, 1200, partner.Airline);
                    return;
                case 1200:
                    if (ours) d.Choices(1600, 1602);
                    else PartnerSays(d, sym < -400 ? 1602 : sym < -100 ? 1601 : 1600);
                    return;
                case 2200: case 2201: case 2202:
                    d.Par2 ^= 1;                // toggles twice: the turn does not change
                    ours = d.Par2 == 1;
                    goto case 1001;
                case 1001: case 1600: case 2500: case 2501: case 2502: case 2503:
                    if (ours) MainList(d, id == 1600); else PartnerTopic(d);
                    return;
                case 2000: case 2010: case 2020: case 2030:
                    if (ours) d.Choices(2500, 2503);
                    else
                    {
                        long money = partner.MoneyPast[partner.MoneyPast.Length - 1];
                        PartnerSays(d, money > 10000000 ? 2500 : money > 1000000 || w.Clock.Date == 0 ? 2501 : money >= 0 ? 2502 : 2503);
                    }
                    return;
                case 2001: case 2011: case 2021: case 2031:
                    if (ours) { if (coop) d.Choices(20210, 20213); else d.Choices(20200, 20201); }
                    else PartnerSays(d, coop && sym < -20 ? 20211 : 20200);
                    return;
                case 2012:
                {
                    // The gift (rooms-and-ui-3b §19): the partner thanks by its sympathy before the gift (normal box)
                    // or notices the spiked one; the sim changes the sympathy and takes the box.
                    bool spiked = !w.Local.HasItem(ItemId.Chocolates) && w.Local.HasItem(ItemId.SpikedChocolates);
                    if (!w.Execute(new OfferChocolatesCommand(w.LocalPlayer, d.Par1)).Ok) { d.Par2 ^= 1; MainList(d, false); return; }
                    PartnerSays(d, spiked ? 2202 : sym > 0 ? 2200 : 2201);
                    return;
                }
                case 20200:
                    if (ours) d.Choices(20400, 20401);
                    else PartnerSays(d, sym > 0 ? 20400 : 20401);
                    return;
                case 20400:
                    Effect(d, 30, true);
                    if (ours) MainList(d, false); else PartnerSays(d, 2003);
                    return;
                case 20401:
                    Effect(d, ours ? -10 : -30);
                    if (ours) MainList(d, false); else PartnerSays(d, 20402);
                    return;
                case 20210:
                    Effect(d, -200, false);
                    if (ours) d.PlayerSays(20402); else PartnerSays(d, 20402);
                    return;
                case 20211:
                    Effect(d, 0, false);
                    if (ours) d.PlayerSays(20402); else PartnerSays(d, 20402);
                    return;
                case 20212:
                    Effect(d, 10);
                    if (ours) d.PlayerSays(20403); else PartnerSays(d, 20403);
                    return;
                case 20201: case 20213: case 20403:
                    if (!ours) d.Par2 ^= 1;     // back to our main list
                    MainList(d, false);
                    return;
                case 1601:
                    if (!ours) { Effect(d, -10, sym - 10 < -100 ? false : (bool?)null); }
                    else Effect(d, 0, false);
                    d.End();
                    return;
                case 1602:
                    Effect(d, ours ? 0 : -100, false);
                    d.End();
                    return;
            }
            d.End();   // 2002/2003 (bye), 2499 (bored), 20402 and anything else
        }
    }
}
