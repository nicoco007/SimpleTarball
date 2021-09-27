using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace SimpleTarball.Tests
{
    public class TarballReaderTests
    {
        [Fact]
        public void ReadTarball()
        {
            using TarballReader tarballReader = new("Resources/SampleTarball.tar");
            IEnumerator<TarballEntry> entries = tarballReader.ReadEntries().GetEnumerator();

            Assert.True(entries.MoveNext());
            Assert.NotNull(entries.Current);

            Assert.Equal("file1.txt", entries.Current.FullName);
            Assert.Equal(new DateTimeOffset(2021, 9, 25, 18, 6, 34, TimeSpan.Zero), entries.Current.LastWriteTime);
            Assert.Equal(1162, entries.Current.Length);

            using (Stream stream = entries.Current.OpenRead())
            using (StreamReader reader = new(stream))
            {
                Assert.Equal(File.ReadAllText("Resources/file1.txt"), reader.ReadToEnd());
            }

            Assert.True(entries.MoveNext());
            Assert.NotNull(entries.Current);

            Assert.Equal("file2.txt", entries.Current.FullName);
            Assert.Equal(new DateTimeOffset(2021, 9, 25, 18, 6, 46, TimeSpan.Zero), entries.Current.LastWriteTime);
            Assert.Equal(1621, entries.Current.Length);

            using (Stream stream = entries.Current.OpenRead())
            using (StreamReader reader = new(stream))
            {
                Assert.Equal(File.ReadAllText("Resources/file2.txt"), reader.ReadToEnd());
            }

            Assert.False(entries.MoveNext());
        }
    }
}
