using System;
using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class RecoveryDeviceGraphTests
    {
        static RecoveryDevice Device(string id, RecoveryDeviceKind kind = RecoveryDeviceKind.Forklift) =>
            new RecoveryDevice(id, kind, new Cell(-1, 1), 1, "warehouse-lift", 3.7f);

        [Theory]
        [InlineData(RecoveryDeviceKind.Forklift)]
        [InlineData(RecoveryDeviceKind.Arm)]
        [InlineData(RecoveryDeviceKind.Tree)]
        public void DefaultGraphHasIndependentRecoveryEventEntries(RecoveryDeviceKind kind)
        {
            var graph = RecoveryDeviceCommands.DefaultGraph(kind, "device");
            var other = RecoveryDeviceCommands.DefaultGraph(kind, "device");
            Assert.Equal("device", graph.OwnerId);
            Assert.Equal(new[] { TriggerIds.RecoveryStart, TriggerIds.RecoveryEnd }, graph.Nodes.Select(n => n.Kind));
            Assert.Empty(graph.Edges);
            Assert.NotEqual(graph.Id, other.Id);
            Assert.Empty(graph.Nodes.Select(n => n.Id).Intersect(other.Nodes.Select(n => n.Id)));
        }

        [Fact]
        public void GraphEditRoundTripsAndUndoRedoPreservesIds()
        {
            var holder = new DraftHolder(new RoomDraftModel(new Room("room", 4, 4)));
            var history = new CommandHistory();
            history.Execute(RecoveryDeviceCommands.Place(holder, Device("device")));
            var graph = RecoveryDeviceCommands.DefaultGraph(RecoveryDeviceKind.Forklift, "device");
            history.Execute(RecoveryDeviceCommands.SetGraph(holder, graph));
            var back = RoomCodec.Decode(RoomCodec.Encode(holder.Draft));
            Assert.Equal("device", Assert.Single(back.Graphs).OwnerId);
            Assert.True(back.Room.Ids.Contains(graph.Id));
            history.Undo();
            Assert.Empty(holder.Draft.Graphs);
            Assert.False(holder.Draft.Room.Ids.Contains(graph.Id));
            history.Redo();
            Assert.Equal(graph.Id, Assert.Single(holder.Draft.Graphs).Id);
            var updated = new BehaviorGraph(graph.Id, "device", graph.Nodes, graph.Edges);
            history.Execute(RecoveryDeviceCommands.SetGraph(holder, updated));
            Assert.Single(holder.Draft.Graphs);
            history.Undo();
            Assert.Equal(graph.Id, Assert.Single(holder.Draft.Graphs).Id);
        }

        [Fact]
        public void DeviceReplacementAndDeletionManageOwnedGraphsAndUndo()
        {
            var holder = new DraftHolder(new RoomDraftModel(new Room("room", 4, 4)));
            var history = new CommandHistory();
            history.Execute(RecoveryDeviceCommands.Place(holder, Device("device")));
            var graph = RecoveryDeviceCommands.DefaultGraph(RecoveryDeviceKind.Forklift, "device");
            history.Execute(RecoveryDeviceCommands.SetGraph(holder, graph));
            history.Execute(RecoveryDeviceCommands.Place(holder, Device("device", RecoveryDeviceKind.Arm), true));
            Assert.Single(holder.Draft.Graphs);
            history.Execute(RecoveryDeviceCommands.Place(holder, Device("other"), true));
            Assert.Empty(holder.Draft.Graphs);
            Assert.False(holder.Draft.Room.Ids.Contains(graph.Id));
            history.Undo();
            Assert.Equal("device", Assert.Single(holder.Draft.Graphs).OwnerId);
            Assert.True(holder.Draft.Room.Ids.Contains(graph.Id));
            history.Redo();
            Assert.Empty(holder.Draft.Graphs);
            history.Undo();
            history.Execute(RecoveryDeviceCommands.Delete(holder));
            Assert.Empty(holder.Draft.Graphs);
            history.Undo();
            Assert.Equal(graph.Id, Assert.Single(holder.Draft.Graphs).Id);
        }

        [Fact]
        public void GraphEditRejectsMissingDeviceAndForeignIdsWithoutChanges()
        {
            var holder = new DraftHolder(new RoomDraftModel(new Room("room", 4, 4)));
            var history = new CommandHistory();
            var graph = RecoveryDeviceCommands.DefaultGraph(RecoveryDeviceKind.Forklift, "device");
            Assert.Throws<InvalidOperationException>(() => history.Execute(RecoveryDeviceCommands.SetGraph(holder, graph)));
            history.Execute(RecoveryDeviceCommands.Place(holder, Device("device")));
            holder.Draft.Room.AddObject("box", ObjectCategories.Box, new Cell(1, 1));
            Assert.Throws<ArgumentException>(() => history.Execute(RecoveryDeviceCommands.SetGraph(holder, new BehaviorGraph("box", "device", null, null))));
            Assert.Throws<InvalidOperationException>(() => history.Execute(RecoveryDeviceCommands.SetGraph(holder, new BehaviorGraph("new", "box", null, null))));
            Assert.Empty(holder.Draft.Graphs);
            Assert.True(holder.Draft.Room.Ids.Contains("box"));
        }
    }
}
