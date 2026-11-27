using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class IssueLocatorTests
    {
        static Issue Make(string rule, string location) => new Issue(rule, Severity.Error, location, "m");

        [Fact]
        public void MissingSpawnPointsAtSpawnToolOnTheRoom()
        {
            var target = IssueLocator.Locate(Make("spawn.missing", "r"));
            Assert.Equal(LocateKind.Room, target.Kind);
            Assert.Equal("r", target.Id);
            Assert.Equal(LocateTool.PlaceSpawn, target.Tool);
        }

        [Fact]
        public void BoundaryGapLocatesTheCellAndOffersWall()
        {
            var target = IssueLocator.Locate(Make("boundary.open", "(3,4)"));
            Assert.Equal(LocateKind.Cell, target.Kind);
            Assert.Equal(new Cell(3, 4), target.Cell);
            Assert.Equal(LocateTool.PaintWall, target.Tool);
        }

        [Fact]
        public void DoorGeometryLocatesTheDoor()
        {
            var target = IssueLocator.Locate(Make("door.geometry", "entry"));
            Assert.Equal(LocateKind.Door, target.Kind);
            Assert.Equal("entry", target.Id);
            Assert.Equal(LocateTool.SetDoor, target.Tool);
        }

        [Fact]
        public void ObjectRulesLocateTheObject()
        {
            var target = IssueLocator.Locate(Make("object.sharedCell", "box-2"));
            Assert.Equal(LocateKind.Object, target.Kind);
            Assert.Equal("box-2", target.Id);
            Assert.Equal(LocateTool.Select, target.Tool);
        }

        [Fact]
        public void MissingRecoveryDeviceLocatesTheRoom()
        {
            var target = IssueLocator.Locate(Make("recoveryDevice.missing", "r"));
            Assert.Equal(LocateKind.Room, target.Kind);
            Assert.Equal("r", target.Id);
            Assert.Equal(LocateTool.None, target.Tool);
        }

        [Fact]
        public void MissingTargetsAndEntryOfferTheirTools()
        {
            Assert.Equal(LocateTool.PlaceTarget, IssueLocator.Locate(Make("target.none", "r")).Tool);
            Assert.Equal(LocateTool.SetDoor, IssueLocator.Locate(Make("entry.missing", "r")).Tool);
        }

        [Fact]
        public void UnknownRuleStillLocatesWithoutTool()
        {
            var target = IssueLocator.Locate(Make("something.new", "r"));
            Assert.Equal(LocateTool.None, target.Tool);
            Assert.Equal(LocateKind.Room, target.Kind);
        }
    }
}
