using OpenAT.Formats;
using OpenAT.Sim;
using OpenAT.Sim.Core;
using OpenAT.Sim.Data;
using OpenAT.Sim.Hall;
using OpenAT.Sim.Items;
using Xunit;

namespace OpenAT.Tests
{
    /// <summary>Sim additions for the special rooms (rooms-and-ui-8, rooms-and-ui-3b §13b, items-security-special.md §2.5).</summary>
    public class Rooms3SpecialRoomsTests
    {
        static World Running()
        {
            var w = ItemsWorld.Make();
            w.Clock.Frozen = false;
            w.Clock.State = DayState.Playing;
            return w;
        }

        [Fact]
        public void Drinker_TalksStaffOutOfStrike_CountdownFour()
        {
            var w = Running();
            var p = w.Players[0];
            // Without his trust nothing happens.
            p.StrikeHours = 5;
            Assert.False(w.Execute(new DrinkerHurriesStrikeEndCommand(0)).Ok);
            p.TrinkerTrust = true;
            // He must have started first (opening of the dialog: countdown 10, end type 3).
            Assert.False(w.Execute(new DrinkerHurriesStrikeEndCommand(0)).Ok);
            Assert.True(w.Execute(new DrinkerEndsStrikeCommand(0)).Ok);
            Assert.Equal(10, p.StrikeEndCountdown);
            Assert.Equal(3, p.StrikeEndType);
            // Clicking his line 6000: 4 minutes left.
            Assert.True(w.Execute(new DrinkerHurriesStrikeEndCommand(0)).Ok);
            Assert.Equal(4, p.StrikeEndCountdown);
            Assert.Equal(3, p.StrikeEndType);
            // No strike, no talk.
            p.StrikeHours = 0;
            Assert.False(w.Execute(new DrinkerHurriesStrikeEndCommand(0)).Ok);
        }

        static GameData _data;
        static HallData _hall;

        static bool LoadHall()
        {
            if (_hall != null) return true;
            var install = GameInstall.TryLocate();
            if (install == null) return false;
            _hall = HallData.Load(install);
            _data = GameData.Load(install);
            HallModule.Register();
            HallContent.Attach(_data, _hall);
            return true;
        }

        [Fact]
        public void Hall_SecurityOfficeClosedDuringPowerOutage()
        {
            if (!LoadHall()) return;
            var w = World.NewGame(_data, new NewGameSettings { StartTime = 0, Seed = 7 });
            if (w.Hall == null) HallModule.EnsureHall(w, _hall);
            Assert.Equal(-1, w.Hall.ClosedMenu(RoomIds.Security));
            w.Items.SecurityOutageDays = 3;
            // MENU_CLOSED sub-type 8 ("security power cut", Misc 3208).
            Assert.Equal(8, w.Hall.ClosedMenu(RoomIds.Security));
            var got = new System.Collections.Generic.List<HallRequest>();
            w.Events.Subscribe<HallRequest>(e => got.Add(e));
            Assert.False(w.Hall.WalkToRoom(0, RoomIds.Security));
            Assert.Single(got);
            Assert.Equal(HallRequest.Closed, got[0].Kind);
            Assert.Equal(8, got[0].Arg);
            // The counter runs out at night; then the office opens again.
            w.Items.SecurityOutageDays = 0;
            Assert.Equal(-1, w.Hall.ClosedMenu(RoomIds.Security));
        }

        [Fact]
        public void Pliers_AtSecurityDoor_ClosesOfficeForThreeDays()
        {
            var w = Running();
            var p = w.Players[0];
            p.SecurityFlags = 0x1FF;
            w.Players[1].SecurityFlags = 1 << SecurityBits.Office;
            Assert.True(w.Execute(new PickUpItemCommand(0, ItemId.Pliers)).Ok);
            var c = new UseItemCommand(0, p.Items.IndexOf(ItemId.Pliers), new ItemUseContext { Room = ItemRooms.Airport, NearSecurityDoor = true, UpperFloor = false });
            Assert.True(w.Execute(c).Ok);
            Assert.Equal(3, w.Items.SecurityOutageDays);
            Assert.Equal(0, p.SecurityFlags);
            Assert.Equal(0, w.Players[1].SecurityFlags);
            // While closed the security menu refuses every click.
            Assert.Equal("security-closed", w.Execute(new ToggleSecurityCommand(0, SecurityBits.Office)).Reason);
        }
    }
}
