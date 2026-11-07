using OpenAT.Formats;
using OpenAT.Sim;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using OpenAT.Sim.Hall;
using OpenAT.Sim.Items;
using OpenAT.Sim.Missions;
using Xunit;

namespace OpenAT.Tests
{
    /// <summary>
    /// The trade chains as the rooms drive them (items-security-special.md §2.5, §3.3, §3.4; rooms-and-ui-9 §5.3 a,
    /// §5.6): the reward commands the dialogs use, the chocolates gift of the rival talk, and the hall's walk-in
    /// side effects (pliers at a rival's office door, the glowing coal at a bombed office).
    /// </summary>
    public class ItemsChainTests
    {
        static World Running()
        {
            var w = ItemsWorld.Make();
            w.Clock.Frozen = false;
            w.Clock.State = DayState.Playing;
            return w;
        }

        [Fact]
        public void Horseshoe_IsADialogReward_NotAFoundItem()
        {
            var w = Running();
            var p = w.Players[0];
            p.DutyTrust = true;
            // The duty-free node 802 must hand it out as a reward; the found-item command does not know it.
            Assert.Equal("not-a-found-item", w.Execute(new PickUpItemCommand(0, ItemId.Horseshoe)).Reason);
            Assert.True(w.Execute(new ReceiveRewardCommand(0, ItemId.Horseshoe)).Ok);
            Assert.True(p.HasItem(ItemId.Horseshoe));
            Assert.False(w.Execute(new ReceiveRewardCommand(0, ItemId.Horseshoe)).Ok);   // one at a time
        }

        [Fact]
        public void Oil_OnlyWithTheMechanicsTrust_ThenOilsTheGlobe()
        {
            var w = Running();
            var p = w.Players[0];
            Assert.Equal("mech-20", w.Execute(new PickUpItemCommand(0, ItemId.Oil)).Reason);
            p.MechTrust = 1;
            Assert.True(w.Execute(new PickUpItemCommand(0, ItemId.Oil)).Ok);
            Assert.False(w.Execute(new PickUpItemCommand(0, ItemId.Oil)).Ok);
            var c = new UseItemCommand(0, p.Items.IndexOf(ItemId.Oil), new ItemUseContext { Room = ItemRooms.Globe });
            Assert.True(w.Execute(c).Ok);
            Assert.True(p.GlobeOiled);
            Assert.False(p.HasItem(ItemId.Oil));
        }

        [Fact]
        public void Chocolates_ToARobot_NormalFirst_ClampedAt1000()
        {
            var w = Running();
            var p = w.Players[0];
            var robot = w.Players[1];
            var sym = w.Rules.Get<IRivalSympathy>();
            sym.Change(w, 1, 0, 995 - sym.Get(w, 1, 0));
            p.Items.Add(ItemId.Chocolates);
            Assert.True(w.Execute(new OfferChocolatesCommand(0, 1)).Ok);
            Assert.Equal(1000, sym.Get(w, 1, 0));
            Assert.False(p.HasItem(ItemId.Chocolates));
            Assert.Equal("no-chocolates", w.Execute(new OfferChocolatesCommand(0, 1)).Reason);
            p.Items.Add(ItemId.SpikedChocolates);
            Assert.True(w.Execute(new OfferChocolatesCommand(0, 1)).Ok);
            Assert.Equal(990, sym.Get(w, 1, 0));
            Assert.Equal(0, robot.Drunk);
            // A human rival gets drunk from the spiked box.
            w.Players[2].Owner = Owner.Human;
            p.Items.Add(ItemId.SpikedChocolates);
            Assert.True(w.Execute(new OfferChocolatesCommand(0, 2)).Ok);
            Assert.Equal(ItemEngine.DrunkPerDose, w.Players[2].Drunk);
        }

        // ------------------------------------------------------------------ hall walk-in (needs the install)

        static GameData _data;
        static HallData _hall;

        static bool Load()
        {
            if (_data != null) return true;
            var install = GameInstall.TryLocate();
            if (install == null) return false;
            _hall = HallData.Load(install);
            _data = GameData.Load(install);
            HallModule.Register();
            BriefingRecorder.Register();
            HallContent.Attach(_data, _hall);
            return true;
        }

        static World HallWorld()
        {
            var w = World.NewGame(_data, new NewGameSettings { Seed = 4711 });
            if (w.Hall == null) HallModule.EnsureHall(w, _hall);
            for (int i = 0; i < 2_000_000 && !(w.Clock.State == DayState.Morning && w.Clock.Frozen); i++) w.Tick();
            Assert.True(w.Execute(new StartWorkingDayCommand()).Ok);
            Assert.True(w.Execute(new ReturnToHallCommand(0)).Ok);
            Rooms1HallWalkTests.StayInHall(w);
            return w;
        }

        [Fact]
        public void Pliers_AtARivalsOfficeDoor_CutItsPower()
        {
            if (!Load()) return;
            var w = HallWorld();
            var me = w.Players[0];
            Assert.True(me.Items.Add(ItemId.Pliers));
            int wrong = 0;
            w.Events.Subscribe<HallRequest>(e => { if (e.Player == 0 && e.Kind == HallRequest.WrongRoom) wrong++; });
            Assert.True(w.Execute(new WalkToRoomCommand(0, RoomIds.OfficeA + 10)).Ok);
            for (int i = 0; i < 8000 && wrong == 0; i++) w.Tick();
            Assert.Equal(1, wrong);
            Assert.Equal(3, w.Players[1].OfficeState);
            Assert.False(me.HasItem(ItemId.Pliers));
            Assert.True(w.Items.OfficePowerCutToday);
        }

        [Fact]
        public void BombedOffice_GivesTheGlowingCoalOncePerDay_AndTheNotice()
        {
            if (!Load()) return;
            var w = HallWorld();
            var me = w.Players[0];
            w.Players[1].OfficeState = 2;
            int notices = 0;
            w.Events.Subscribe<HallRequest>(e => { if (e.Player == 0 && e.Kind == HallRequest.Destroyed) notices++; });
            Assert.True(w.Execute(new WalkToRoomCommand(0, RoomIds.OfficeA + 10)).Ok);
            Assert.Equal(1, notices);
            Assert.True(me.HasItem(ItemId.GlowingCoal));
            Assert.False(w.Items.GlowingCoal);
            Assert.True(w.Execute(new DropItemCommand(0, me.Items.IndexOf(ItemId.GlowingCoal))).Ok);
            Assert.True(w.Execute(new WalkToRoomCommand(0, RoomIds.OfficeA + 10)).Ok);
            Assert.Equal(2, notices);
            Assert.False(me.HasItem(ItemId.GlowingCoal));   // the world item is gone for today
            // An intact office: no notice.
            Assert.True(w.Execute(new WalkToRoomCommand(0, RoomIds.OfficeA + 20)).Ok);
            Assert.Equal(2, notices);
        }
    }
}
