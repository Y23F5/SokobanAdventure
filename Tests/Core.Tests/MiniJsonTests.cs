using System;
using System.Collections.Generic;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class MiniJsonTests
    {
        [Fact]
        public void RoundTripNestedStructure()
        {
            var root = new Dictionary<string, object>
            {
                ["a"] = (long)-3,
                ["b"] = "x",
                ["c"] = new List<object> { (long)1, null, "y" },
                ["d"] = new Dictionary<string, object> { ["k"] = new List<object>() },
            };
            var back = (Dictionary<string, object>)MiniJson.Parse(MiniJson.Write(root));
            Assert.Equal(-3L, back["a"]);
            Assert.Equal("x", back["b"]);
            var list = (List<object>)back["c"];
            Assert.Equal(3, list.Count);
            Assert.Null(list[1]);
            Assert.Empty((List<object>)((Dictionary<string, object>)back["d"])["k"]);
        }

        [Fact]
        public void StringEscapesSurviveRoundTrip()
        {
            string s = "引号\" 反斜杠\\ 换行\n 制表\t 控制\u0001";
            Assert.Equal(s, MiniJson.Parse(MiniJson.Write(s)));
        }

        [Fact]
        public void ParsesUnicodeEscape()
        {
            Assert.Equal("A", MiniJson.Parse("\"\\u0041\""));
        }

        [Fact]
        public void DuplicateKeysAreRejected()
        {
            Assert.Throws<FormatException>(() => MiniJson.Parse("{\"a\":1,\"a\":2}"));
        }

        [Fact]
        public void MalformedInputIsRejected()
        {
            foreach (var bad in new[] { "{", "[1,", "{\"a\"}", "\"abc", "{1:2}", "tru", "[1 2]", "{} x", "" })
                Assert.Throws<FormatException>(() => MiniJson.Parse(bad));
        }

        [Fact]
        public void NonIntegerNumbersAreRejected()
        {
            Assert.Throws<FormatException>(() => MiniJson.Parse("1.5"));
            Assert.Throws<FormatException>(() => MiniJson.Parse("1e3"));
        }

        [Fact]
        public void ParsesBooleanLiterals()
        {
            var back = (List<object>)MiniJson.Parse("[true,false,{\"k\":true}]");
            Assert.Equal(true, back[0]);
            Assert.Equal(false, back[1]);
            Assert.Equal(true, ((Dictionary<string, object>)back[2])["k"]);
        }

        [Fact]
        public void WritesBooleanLiterals()
        {
            Assert.Equal("[true,false]", MiniJson.Write(new List<object> { true, false }));
        }

        [Fact]
        public void BooleanRoundTrip()
        {
            var root = new Dictionary<string, object> { ["a"] = true, ["b"] = false };
            var back = (Dictionary<string, object>)MiniJson.Parse(MiniJson.Write(root));
            Assert.Equal(true, back["a"]);
            Assert.Equal(false, back["b"]);
        }

        [Fact]
        public void TruncatedBooleanIsStillRejected()
        {
            foreach (var bad in new[] { "tru", "fals", "[tr]", "truex" })
                Assert.Throws<FormatException>(() => MiniJson.Parse(bad));
        }

        [Fact]
        public void WriteRejectsUnsupportedType()
        {
            Assert.Throws<ArgumentException>(() => MiniJson.Write(1.5));
        }
    }
}
