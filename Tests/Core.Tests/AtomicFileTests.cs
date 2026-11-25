using System;
using System.IO;
using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class AtomicFileTests : IDisposable
    {
        readonly string dir = Path.Combine(Path.GetTempPath(), "sokoban-atomic-" + Guid.NewGuid().ToString("N"));

        public AtomicFileTests() { Directory.CreateDirectory(dir); }

        public void Dispose() { Directory.Delete(dir, true); }

        string PathOf(string name) => Path.Combine(dir, name);

        [Fact]
        public void NewFileIsCreated()
        {
            AtomicFile.Write(PathOf("a.json"), "hello");
            Assert.Equal("hello", File.ReadAllText(PathOf("a.json")));
        }

        [Fact]
        public void ExistingFileIsReplaced()
        {
            File.WriteAllText(PathOf("a.json"), "old");
            AtomicFile.Write(PathOf("a.json"), "new");
            Assert.Equal("new", File.ReadAllText(PathOf("a.json")));
        }

        [Fact]
        public void ValidatorReadsTheCandidateTextBeforeReplacement()
        {
            File.WriteAllText(PathOf("a.json"), "old");
            string seen = null;
            AtomicFile.Write(PathOf("a.json"), "new", temp =>
            {
                seen = File.ReadAllText(temp);
                Assert.Equal("old", File.ReadAllText(PathOf("a.json")));
            });
            Assert.Equal("new", seen);
        }

        [Fact]
        public void FailedValidationKeepsOriginalAndLeavesNoTempFile()
        {
            File.WriteAllText(PathOf("a.json"), "old");
            Assert.Throws<InvalidDataException>(() =>
                AtomicFile.Write(PathOf("a.json"), "bad", temp => throw new InvalidDataException("rejected")));
            Assert.Equal("old", File.ReadAllText(PathOf("a.json")));
            Assert.Equal(new[] { PathOf("a.json") }, Directory.GetFiles(dir));
        }

        [Fact]
        public void FailedValidationOnNewFileCreatesNothing()
        {
            Assert.Throws<InvalidDataException>(() =>
                AtomicFile.Write(PathOf("b.json"), "bad", temp => throw new InvalidDataException("rejected")));
            Assert.Empty(Directory.GetFiles(dir));
        }

        [Fact]
        public void NoTempFileRemainsAfterSuccess()
        {
            File.WriteAllText(PathOf("a.json"), "old");
            AtomicFile.Write(PathOf("a.json"), "new");
            Assert.Single(Directory.GetFiles(dir));
        }
    }
}
