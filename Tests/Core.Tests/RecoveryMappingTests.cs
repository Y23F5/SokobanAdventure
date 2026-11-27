using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class RecoveryDeviceThemeTests
    {
        [Theory]
        [InlineData("warehouse", RecoveryDeviceKind.Forklift, "warehouse-lift")]
        [InlineData("factory", RecoveryDeviceKind.Arm, "factory-belt")]
        [InlineData("forest", RecoveryDeviceKind.Tree, "forest-decay")]
        public void ExplicitDeviceSurvivesThemeAndCodecRoundTrip(string theme, RecoveryDeviceKind kind, string script)
        {
            var draft = new RoomDraftModel(new Room("room", 5, 5));
            draft.Meta = new RoomMeta("room", theme);
            Assert.Null(draft.RecoveryDevice);
            draft.ReplaceRecoveryDevice(new RecoveryDevice("device", kind, new Cell(-1, 2), 1, script, RecoveryDevice.DefaultDuration));
            var back = RoomCodec.Decode(RoomCodec.Encode(draft));
            Assert.Equal(kind, back.RecoveryDevice.Kind);
            Assert.Equal(script, back.RecoveryDevice.ScriptRef);
            Assert.Equal(RecoveryDevice.DefaultDuration, back.RecoveryDevice.Duration);
        }
    }
}
