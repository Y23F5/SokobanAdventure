using System;
using System.Collections.Generic;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class RecoverySessionTests
    {
        // 7x3 地板，(3,1) 为可开合地形，箱子初始在 (2,1)
        static Room MakeRoom()
        {
            var room = new Room("r", 7, 3);
            for (int x = 0; x < 7; x++)
                for (int y = 0; y < 3; y++)
                    room.SetTerrain(new Cell(x, y), TerrainCategories.Floor);
            room.SetTerrain(new Cell(3, 0), TerrainCategories.Openable, "vine");
            room.SetTerrain(new Cell(3, 1), TerrainCategories.Openable, "vine2");
            room.SetTerrain(new Cell(3, 2), TerrainCategories.Openable, "vine3");
            room.AddObject("spawn", ObjectCategories.Spawn, new Cell(0, 0));
            room.AddObject("box-1", ObjectCategories.Box, new Cell(2, 1));
            room.AddObject("target-1", ObjectCategories.Target, new Cell(5, 1));
            return room;
        }

        static RecoverySession Make(Cell boxAt, Cell player)
        {
            var room = MakeRoom();
            return new RecoverySession(room, new Dictionary<string, Cell> { ["box-1"] = boxAt }, player, new Cell(1, 0));
        }

        [Fact]
        public void ClosingGateWaitsUntilBoxMoves()
        {
            var cell = new Cell(3, 1);
            var session = Make(cell, new Cell(1, 1));
            Assert.True(session.RequestGate(cell, true, true));
            Assert.True(session.RequestGate(cell, false, true));
            Assert.True(session.IsWalkable(cell));
            session.Boxes["box-1"] = new Cell(4, 1);
            session.RetryGateClosures(true);
            Assert.False(session.IsWalkable(cell));
        }

        [Fact]
        public void ClosingGateRelocatesPlayerToUnoccupiedWalkableCell()
        {
            var cell = new Cell(3, 1);
            var session = Make(new Cell(4, 1), cell);
            session.RequestGate(cell, true, true);
            session.RequestGate(cell, false, true);
            Assert.Equal(new Cell(2, 1), session.Player);
            Assert.False(session.IsWalkable(cell));
        }

        [Fact]
        public void StartRejectsNonPositiveDuration()
        {
            Assert.Throws<ArgumentException>(() => Make(new Cell(2, 1), new Cell(0, 0)).Start(0));
        }

        [Fact]
        public void PushingIsForbiddenWhileRecovering()
        {
            var s = Make(new Cell(2, 1), new Cell(0, 0));
            Assert.True(s.CanPush());
            s.Start(3.7f);
            Assert.False(s.CanPush());
        }

        [Fact]
        public void ZoneBlocksEntryAndContainsRoute()
        {
            var s = Make(new Cell(5, 1), new Cell(0, 0));
            s.Start(3.7f);
            Assert.False(s.CanEnter(new Cell(4, 1)));
            Assert.False(s.CanEnter(new Cell(2, 1)));
            Assert.True(s.CanEnter(new Cell(0, 2)));
        }

        [Fact]
        public void PlayerInsideZoneIsMovedAtStart()
        {
            var s = Make(new Cell(5, 1), new Cell(4, 1));
            s.Start(3.7f);
            Assert.DoesNotContain(s.Player, s.Zone);
        }

        [Fact]
        public void OpenableIsOpenOnlyWhileRecovering()
        {
            var s = Make(new Cell(5, 1), new Cell(0, 0));
            Assert.False(s.IsWalkable(new Cell(3, 0)));
            s.Start(3.7f);
            Assert.True(s.IsWalkable(new Cell(3, 0)));
            s.Complete();
            Assert.False(s.IsWalkable(new Cell(3, 0)));
        }

        [Fact]
        public void CompleteReturnsBoxesToInitialOnceOnly()
        {
            var s = Make(new Cell(5, 1), new Cell(0, 0));
            s.Start(3.7f);
            s.Complete();
            Assert.Equal(new Cell(2, 1), s.Boxes["box-1"]);
            s.Complete();
            Assert.Equal(1, s.CompletedCount);
        }

        [Fact]
        public void AdvanceCompletesWhenTimeRunsOut()
        {
            var s = Make(new Cell(5, 1), new Cell(0, 0));
            s.Start(2f);
            Assert.False(s.Advance(1.5f));
            Assert.True(s.Recovering);
            Assert.True(s.Advance(1f));
            Assert.False(s.Recovering);
            Assert.Equal(new Cell(2, 1), s.Boxes["box-1"]);
        }

        [Fact]
        public void PlayerOnOpenableIsMovedWhenItCloses()
        {
            var s = Make(new Cell(5, 1), new Cell(0, 0));
            s.Start(3.7f);
            // 玩家在恢复期间走进开放的可开合地形格
            var field = typeof(RecoverySession).GetProperty("Player");
            field.SetValue(s, new Cell(3, 0));
            s.Complete();
            Assert.NotEqual(new Cell(3, 0), s.Player);
            Assert.True(s.IsWalkable(s.Player));
        }

        [Fact]
        public void PlayerOnBoxInitialCellIsMovedAtCompletion()
        {
            var s = Make(new Cell(5, 1), new Cell(0, 0));
            s.Start(3.7f);
            typeof(RecoverySession).GetProperty("Player").SetValue(s, new Cell(2, 1));
            s.Complete();
            Assert.NotEqual(new Cell(2, 1), s.Player);
        }

        [Fact]
        public void StartTwiceDoesNotRestart()
        {
            var s = Make(new Cell(5, 1), new Cell(0, 0));
            s.Start(3f);
            s.Advance(1f);
            s.Start(3f);
            Assert.Equal(2f, s.Remaining, 3);
        }
    }
}
