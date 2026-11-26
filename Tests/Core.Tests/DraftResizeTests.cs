/**
 * @file DraftResizeTests.cs
 * @brief 验证 RoomResize.Resized 的草稿重载：扩建平移时门格随房间一起平移，门地形与实体 ID 保持一致，主入口、恢复道具与透传字段原样带到新草稿。
 * 每个用例用 BrushFixture 的七乘七草稿平移后，断言门格、门地形、ID 空间、独立副本、越界门抛异常与 NeedsFloor 在新坐标下的判定。
 */
using System;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class DraftResizeTests
    {
        [Fact]
        public void DoorCellsShiftWithTheRoom()
        {
            var next = RoomResize.Resized(BrushFixture.Draft(), 2, 1, 9, 8);
            Assert.Equal("entry", next.Doors[0].Id);
            Assert.Equal(new Cell(2, 4), next.Doors[0].Cell);
            Assert.Equal(Cell.Left, next.Doors[0].Outward);
            Assert.Equal("entry", next.Doors[0].Role);
            Assert.Equal(new Cell(8, 4), next.Doors[1].Cell);
            Assert.Equal(Cell.Right, next.Doors[1].Outward);
        }

        [Fact]
        public void DoorTerrainMatchesDoorInfoAfterShift()
        {
            var next = RoomResize.Resized(BrushFixture.Draft(), 2, 1, 9, 8);
            foreach (var door in next.Doors)
            {
                var terrain = next.Room.TerrainAt(door.Cell);
                Assert.Equal(TerrainCategories.Door, terrain.CategoryId);
                Assert.Equal(door.Id, terrain.EntityId);
            }
            Assert.Equal(TerrainCategories.Void, next.Room.TerrainAt(new Cell(0, 3)).CategoryId);
        }

        [Fact]
        public void IdSpaceStaysConsistent()
        {
            var source = BrushFixture.Draft();
            var next = RoomResize.Resized(source, 2, 1, 9, 8);
            Assert.Equal(source.Room.Ids.Count, next.Room.Ids.Count);
            foreach (var id in source.Room.Ids.Ids)
            {
                Assert.True(next.Room.Ids.Contains(id));
                Assert.Equal(source.Room.Ids.KindOf(id), next.Room.Ids.KindOf(id));
            }
            Assert.Equal("recovery", next.Room.Ids.KindOf("recovery-1"));
        }

        [Fact]
        public void CarriesEntranceRecoveryItemsAndExtras()
        {
            var source = BrushFixture.Draft();
            var next = RoomResize.Resized(source, 1, 1, 8, 8);
            Assert.Equal("entry", next.PrimaryEntranceId);
            Assert.Single(next.RecoveryItems);
            Assert.Same(source.RecoveryItems[0], next.RecoveryItems[0]);
            Assert.Equal("x", next.Extras["note"]);
        }

        [Fact]
        public void ZeroShiftReturnsIndependentCopy()
        {
            var source = BrushFixture.Draft();
            var next = RoomResize.Resized(source, 0, 0, 7, 7);
            Assert.NotSame(source.Room, next.Room);
            next.Room.SetTerrain(new Cell(2, 2), TerrainCategories.Wall);
            Assert.Equal(TerrainCategories.Floor, source.Room.TerrainAt(new Cell(2, 2)).CategoryId);
        }

        [Fact]
        public void DoorFallingOutsideTheNewRoomThrows()
        {
            Assert.Throws<InvalidOperationException>(() => RoomResize.Resized(BrushFixture.Draft(), 0, 0, 5, 7));
        }

        [Fact]
        public void ShiftedDoorInnerCellNeedsFloor()
        {
            var next = RoomResize.Resized(BrushFixture.Draft(), 2, 1, 9, 8);
            Assert.True(PlacementRules.NeedsFloor(next, new Cell(3, 4)));
            Assert.False(PlacementRules.NeedsFloor(next, new Cell(1, 3)));
        }
    }
}
