using OpenAT.Sim;
using OpenAT.Sim.Items;
using OpenAT.View.Render;
using OpenAT.View.Rooms.Framework;
using OpenAT.View.Screens;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using UnityEngine;

namespace OpenAT.View.Rooms.DutyFree
{
    /// <summary>
    /// Duty free shop (rooms-and-ui-7 §9): the chewing saleswoman, goods on the shelves with item tips
    /// (violin case, filofax, mobile phone, liquor, candy), the laptop counter (model upgrades, one sale per
    /// day, virus repair) and the horseshoe hook. Purchases go through the items domain
    /// (<see cref="BuyDutyFreeCommand"/>, <see cref="BuyLaptopCommand"/>, <see cref="LaptopRepairCommand"/>).
    /// </summary>
    [Room(RoomIds.DutyFree)]
    [ScreenName("dutyfree")]
    public sealed class DutyFreeRoom : RoomScreen
    {
        const string Lib = "room/dutyfree.gli";
        protected override string Library => Lib;
        protected override string Background => "DUTYFREE";
        protected override int DefaultDialogPartner => Talkers.DutyFree;
        const int ClickLaptop = 10, ClickWoman = 111, ClickGun = 400, ClickFilofax = 500, ClickPhone = 600, ClickBeer = 700, ClickHorseshoe = 800, ClickCandy = 801;

        SmackerPerson _woman;
        int _tipItem = -1, _tipX;

        World W => App.Session?.World;
        Player P => App.Session?.Local;

        protected override void OnCreate()
        {
            int x = 310, y = 47;
            _woman = new SmackerPerson(Assets, App.Audio);
            _woman.Clip(0, "DuKau.smk", x, y, Mood.Idle, "A6:0 A4:1 A3:2 E1:6 E1:7", 2, 4, sound: "dukau.raw");
            _woman.Clip(1, "DuBlase.smk", x, y, Mood.Idle, "A1:0", canCancel: false, sound: "dublase.raw");
            _woman.Clip(2, "DuNagelH.smk", x, y, Mood.Idle, "A1:3", canCancel: false);
            _woman.Clip(3, "DuNagelF.smk", x, y, Mood.Idle, "A1:4", 5, 5, sound: "dunagelf.raw");
            _woman.Clip(4, "DuNagelG.smk", x, y, Mood.Idle, "A6:3 A1:5 E1:5");
            _woman.Clip(5, "DuNagelZ.smk", x, y, Mood.Idle, "A1:0 E1:6 E1:7", canCancel: false);
            _woman.Clip(6, "DuRede.smk", x, y, Mood.Talking, "A1:6 E1:7 E1:0");
            _woman.Clip(7, "DuRedeW.smk", x, y, Mood.Listening, "A9:7 A1:8 E1:6 E1:0");
            _woman.Clip(8, "DuRedeK.smk", x, y, Mood.Listening, "A1:7 E1:6 E1:0", 1, 1, 1, 1);
        }

        protected override void OnRoomEnter() => App.Audio.Play("SOUND/windchim.raw");

        public override SmackerPerson PartnerAnimation(int talker, DialogMedium medium) => talker == Talkers.DutyFree ? _woman : null;

        public override void Animate(float dt) => _woman.Update(dt);

        bool Offered(int item) => W != null && ItemEngine.Offered(W, P, item);
        bool HorseshoeOnHook => P != null && !P.HasItem(ItemId.Horseshoe) && !P.TrinkerTrust;
        bool LaptopForSale => P != null && (P.LaptopQuality < 4 || !P.HasItem(ItemId.Laptop));

        protected override void Hotspots(InputState input)
        {
            _tipItem = -1;
            if (W == null) return;
            // Registered from lowest to highest priority (the last hovered one wins).
            if (Offered(ItemId.Beer) && Hot(258, 289, 361, 344, ClickBeer)) Tip(ItemId.Beer, 700);
            if (Offered(ItemId.Phone) && Hot(545, 334, 582, 439, ClickPhone)) Tip(ItemId.Phone, 600);
            if (Offered(ItemId.Filofax) && Hot(346, 306, 445, 357, ClickFilofax)) Tip(ItemId.Filofax, 500);
            if (Offered(ItemId.Chocolates) && Hot(329, 247, 395, 309, ClickCandy)) Tip(ItemId.Chocolates, 801, 68);
            if (Offered(ItemId.MachineGun) && Hot(280, 196, 350, 287, ClickGun)) Tip(ItemId.MachineGun, 400);
            Hot(399, 55, 492, 301, ClickWoman);
            if (LaptopForSale && (Hot(448, 327, 542, 439, ClickLaptop) | Hot(396, 395, 542, 439, ClickLaptop))) Tip(-1, System.Math.Min(3, P.LaptopQuality));
            if (HorseshoeOnHook) Hot(11, 33, 42, 68, ClickHorseshoe);
            Exit(493, 52, 570, 224);
        }

        int _tipText;
        void Tip(int item, int textBase, int x = 48) { _tipItem = item < 0 ? 1000 : item; _tipText = textBase; _tipX = x; }

        protected override void OnClick(int id)
        {
            var w = W; var p = P;
            switch (id)
            {
                case ClickWoman:
                    if (p.LaptopVirus >= 1 && p.LaptopVirus <= 3) StartDialog(Talkers.DutyFree, DialogMedium.Air, 2000 + 10 * (p.LaptopVirus - 1));
                    else StartDialog(Talkers.DutyFree, DialogMedium.Air, 2);
                    return;
                case ClickHorseshoe: StartDialog(Talkers.DutyFree, DialogMedium.Air, 800); return;
                case ClickCandy: StartDialog(Talkers.DutyFree, DialogMedium.Air, 3000); return;
                case ClickGun: Buy(ItemId.MachineGun); return;
                case ClickFilofax: Buy(ItemId.Filofax); return;
                case ClickPhone: Buy(ItemId.Phone); return;
                case ClickBeer: Buy(ItemId.Beer); return;
                case ClickLaptop:
                    switch (ItemEngine.LaptopState(w, p))
                    {
                        case ItemEngine.LaptopOffer.VirusDialog: StartDialog(Talkers.DutyFree, DialogMedium.Air, 2000 + 10 * (p.LaptopVirus - 1)); return;
                        case ItemEngine.LaptopOffer.Locked: StartDialog(Talkers.DutyFree, DialogMedium.Air, 3); return;
                        case ItemEngine.LaptopOffer.SoldToday: StartDialog(Talkers.DutyFree, DialogMedium.Air, 1); return;
                        case ItemEngine.LaptopOffer.MaxModel: return;
                    }
                    if (w.Execute(new BuyLaptopCommand(p.Index)).Ok)
                    {
                        App.Audio.Play("SOUND/pay.raw");
                        if (p.LaptopQuality < 4) StartDialog(Talkers.DutyFree, DialogMedium.Air, 4);
                    }
                    return;
            }
        }

        void Buy(int item)
        {
            if (W.Execute(new BuyDutyFreeCommand(P.Index, item)).Ok) App.Audio.Play("SOUND/pay.raw");
        }

        protected override void DrawRoom(Screen2D s)
        {
            int ox = RX;
            if (Offered(ItemId.MachineGun)) s.Draw(Assets.Texture(Lib, "KONTRA"), ox + 275, 197);
            if (P != null && (P.HasItem(ItemId.Horseshoe) || P.TrinkerTrust)) s.Draw(Assets.Texture(Lib, "OHNEHUF"), ox + 14, 36);
            s.Draw(Assets.Texture(Lib, "TRANS"), ox + 432, 70, new Color32(255, 255, 255, 90));
            s.Draw(Assets.Texture(Lib, "OPAQUE"), ox + 432, 70);
            if (W != null && (W.Clock.Date <= 7 || W.Items.LaptopSoldTo != -1)) s.Draw(Assets.Texture(Lib, "SCHILDE"), ox + 434, 387);
            _woman.Draw(s, ox);
        }

        protected override void DrawOverlay(Screen2D s)
        {
            if (_tipItem < 0 || IsDialogOpen || Menus.IsOpen) return;
            var bg = Assets.Texture("gli/tip.gli", "BLOC1");
            int x = RX + _tipX, y = 94;
            s.Draw(bg, x, y);
            var f = App.Ui.Kit.SmallBlack;
            string T(int id) => Assets.StdText.Get("Item", id) ?? "";
            f.Draw(s, x + 32, y + 14, T(1000 + _tipText));
            f.Draw(s, x + 32, y + 30, T(100));
            f.Draw(s, x + 184, y + 30, UiKit.Money(long.TryParse(T(2000 + _tipText), out var v) ? v : 0), TextAlign.Right);
            f.DrawBlock(s, new RectInt(x + 32, y + 48, 156, 150), T(3000 + _tipText), Color.white);
        }
    }

    /// <summary>Duty free saleswoman (rooms-and-ui-3b §11, rooms-and-ui-7 §9).</summary>
    [DialogScript(Talkers.DutyFree)]
    public sealed class DutyFreeDialog : DialogTree
    {
        public DutyFreeDialog() : base("Duty")
        {
            Open(d =>
            {
                var w = d.World; var p = d.Player;
                switch (d.Par1)
                {
                    case 1:
                        if (w.Items.LaptopSoldTo == p.Index) d.Say(99);
                        else d.Say(100, w.Players[Mathf.Clamp(w.Items.LaptopSoldTo, 0, 3)].Airline);
                        return;
                    case 3:
                        if (8 - w.Clock.Date > 1) { d.Say(97); d.ShowNumbers(9990097, (long)(8 - w.Clock.Date)); }
                        else d.Say(98);
                        return;
                    case 4: d.Say(1010); return;
                    case 800: d.Say(p.DutyTrust ? 802 : 800); return;
                    case 801: d.Say(801); return;   // the bra was given (the items domain sets DutyTrust)
                    case 2000: d.PlayerSays(2000); return;
                    case 2010: d.Say(2010); return;
                    case 2020:
                        d.World.Execute(new LaptopRepairCommand(p.Index));
                        d.Say(2020);
                        return;
                    case 3000: d.Say(3000); return;
                }
                if (d.Par1 >= 10000) { d.Say(d.Par1); return; }
                d.Say(1000);
            });
            On(1000, d => d.PlayerSays(1001));
            On(1001, d => d.Say(1002));
            On(1002, d => d.Say(d.World.Clock.Date <= 7 || d.World.Items.LaptopSoldTo != -1 ? 1003 : 1004));
            On(1003, d => d.Say(1004));
            // 802: the horseshoe is a reward of the bra chain (DutyTrust), not a found item.
            On(802, d => { d.World.Execute(new ReceiveRewardCommand(d.Player.Index, ItemId.Horseshoe)); d.End(); });
            On(2000, d => { d.World.Execute(new LaptopRepairCommand(d.Player.Index)); d.Say(2001); });
            On(3000, d => d.Choices(3001, 3003));
            On(3001, d => { if (d.World.Execute(new BuyDutyFreeCommand(d.Player.Index, ItemId.SpikedChocolates)).Ok) d.Stage.App.Audio.Play("SOUND/pay.raw"); d.End(); });
            On(3002, d => { if (d.World.Execute(new BuyDutyFreeCommand(d.Player.Index, ItemId.Chocolates)).Ok) d.Stage.App.Audio.Play("SOUND/pay.raw"); d.End(); });
        }
    }
}
