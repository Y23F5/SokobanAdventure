/**
 * @file AssetPackManifestTests.cs
 * @brief 使用导出清单代表格式验证分类和字段，并核对尺寸分档与 v1 存档继续拒绝小数。
 */
using System;
using Sokoban.Core;
using Xunit;

public sealed class AssetPackManifestTests
{
    [Fact]
    public void ClassifiesRoomPartsAndReadsLogicalFootprintAndSourceCell()
    {
        const string json = "{\"assets\":[" +
            "{\"file\":\"rooms/depot/wall-cell-standard.glb\",\"bounds_size_xyz\":[1,1.25,1e0]," +
            "\"grid_footprint_xz\":[1,1],\"logical_footprint_xz\":[1,1],\"source_cell_xz\":[-2,4]}," +
            "{\"file\":\"rooms/foundry/backdrop.glb\",\"bounds_size_xyz\":[5,4,7],\"grid_footprint_xz\":[5,7]}," +
            "{\"file\":\"log-bundle.glb\",\"bounds_size_xyz\":[0.51,0.84,0.65],\"grid_footprint_xz\":[1,1]}]}";
        var parts = AssetPackManifest.Parse(json);
        Assert.Collection(parts, wall =>
        {
            Assert.Equal(TerrainCategories.Wall, wall.Category);
            Assert.Equal("warehouse", wall.Theme);
            Assert.Equal(new Cell(1, 1), wall.LogicalFootprint);
            Assert.Equal(new Cell(-2, 4), wall.SourceCell);
            Assert.Equal(1.25, wall.Bounds[1]);
        }, backdrop =>
        {
            Assert.Equal(AssetPackManifest.Other, backdrop.Category);
            Assert.Equal("factory", backdrop.Theme);
        }, unclassified =>
        {
            Assert.Equal(AssetPackManifest.Decoration, unclassified.Category);
            Assert.Null(unclassified.Theme);
        });
    }

    [Fact]
    public void ManifestCategoryDeterminesBoxIdentityWithoutFilename()
    {
        const string json = @"{""assets"": [
            {""file"":""model-001.glb"",""category"":""box"",""theme"":""factory"",
             ""bounds_size_xyz"":[0.8,0.75,0.8],""grid_footprint_xz"":[1,1]},
            {""file"":""log-bundle.glb"",""category"":""decoration"",
             ""bounds_size_xyz"":[0.8,0.75,0.8],""grid_footprint_xz"":[1,1]}
        ]}";
        var parts = AssetPackManifest.Parse(json);
        Assert.Equal(ObjectCategories.Box, parts[0].Category);
        Assert.Equal("factory", parts[0].Theme);
        Assert.Equal(AssetPackManifest.Decoration, parts[1].Category);
        Assert.Null(parts[1].Theme);
        Assert.Throws<FormatException>(() => AssetPackManifest.Parse(json.Replace(@"""category"":""box""", @"""category"":""unknown""")));
    }

    [Fact]
    public void DimensionsHaveCategorySpecificLimitsAndRoomParserStillRequiresIntegers()
    {
        Assert.Null(AssetPackManifest.SizeIssue(TerrainCategories.Wall, "wall.glb", 0.85, 1.15));
        Assert.Contains("0.84", AssetPackManifest.SizeIssue(TerrainCategories.Wall, "wall.glb", 0.84, 1));
        Assert.Null(AssetPackManifest.SizeIssue(ObjectCategories.Box, "box.glb", 0.4, 0.6));
        Assert.Contains("1.16", AssetPackManifest.SizeIssue(ObjectCategories.Box, "box.glb", 1, 1.16));
        Assert.Null(AssetPackManifest.SizeIssue(AssetPackManifest.Decoration, "decoration.glb", 10, 12));
        Assert.Throws<FormatException>(() => MiniJson.Parse("{\"height\": 0.5}"));
        Assert.Throws<FormatException>(() => AssetPackManifest.Parse("{\"assets\":[{\"file\":\"../bad.glb\"}]}"));
        Assert.Equal(1, AssetPackManifest.VariantIndex("warehouse", new Cell(-3, 4), 5));
    }
}
