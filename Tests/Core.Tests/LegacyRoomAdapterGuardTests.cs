using System;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class LegacyRoomAdapterGuardTests
    {
        static LegacyRoomData Sample() => new LegacyRoomData
        {
            LevelId = "sample",
            Width = 5,
            Height = 5,
            RowsNorthToSouth = new[] { "#####", "#...#", ".....", "#...#", "#####" },
            Doors = new[] { new LegacyDoor("entry", new Cell(0, 2), "W", "entry") },
            PrimaryEntranceId = "entry",
            Spawn = new LegacyEntity("spawn", new Cell(2, 2)),
        };

        [Fact]
        public void ValidSampleConverts()
        {
            Assert.Equal("sample", LegacyRoomAdapter.ToDraft(Sample()).Room.Id);
        }

        [Theory]
        [InlineData(0, 5)]
        [InlineData(5, 0)]
        [InlineData(-1, 5)]
        [InlineData(5, -3)]
        [InlineData(0, 0)]
        public void NonPositiveSizeIsFormatException(int width, int height)
        {
            var data = Sample();
            data.Width = width;
            data.Height = height;
            data.RowsNorthToSouth = height > 0 ? new string[height] : new string[0];
            for (int i = 0; i < data.RowsNorthToSouth.Length; i++) data.RowsNorthToSouth[i] = new string('.', Math.Max(width, 0));
            data.Doors = new LegacyDoor[0];
            data.Spawn = null;
            Assert.Throws<FormatException>(() => LegacyRoomAdapter.ToDraft(data));
        }

        [Fact]
        public void ZeroSizeWithEmptyRowsIsFormatException()
        {
            var data = Sample();
            data.Width = 0;
            data.Height = 0;
            data.RowsNorthToSouth = new string[0];
            data.Doors = new LegacyDoor[0];
            data.Spawn = null;
            Assert.Throws<FormatException>(() => LegacyRoomAdapter.ToDraft(data));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t")]
        public void BlankLevelIdIsFormatException(string levelId)
        {
            var data = Sample();
            data.LevelId = levelId;
            Assert.Throws<FormatException>(() => LegacyRoomAdapter.ToDraft(data));
        }
    }
}
