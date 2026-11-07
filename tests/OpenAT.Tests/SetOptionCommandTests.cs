using OpenAT.Sim;
using Xunit;

namespace OpenAT.Tests
{
    public class SetOptionCommandTests
    {
        [Fact]
        public void SwitchesABooleanOption()
        {
            var o = new GameOptions();
            var c = new SetOptionCommand(nameof(GameOptions.GamepadSupport), false);
            var w = World.NewGame(OpsWorld.Data(), new NewGameSettings(), o);
            Assert.True(w.Execute(c).Ok);
            Assert.False(w.Options.GamepadSupport);
            Assert.True(w.Execute(new SetOptionCommand(nameof(GameOptions.GamepadSupport), true)).Ok);
            Assert.True(w.Options.GamepadSupport);
        }

        [Fact]
        public void RefusesUnknownOrNonBooleanFields()
        {
            var w = World.NewGame(OpsWorld.Data(), new NewGameSettings(), new GameOptions());
            Assert.False(w.Execute(new SetOptionCommand("NoSuchOption", true)).Ok);
            Assert.False(w.Execute(new SetOptionCommand(null, true)).Ok);
        }
    }
}
