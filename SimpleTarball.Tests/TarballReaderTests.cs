using System;
using System.Collections.Generic;
using Xunit;

namespace SimpleTarball.Tests
{
    public class TarballReaderTests
    {
        [Fact]
        public void ReadTarball()
        {
            using TarballReader reader = new("Resources/SampleTarball.tar");
            IEnumerator<TarballEntry> entries = reader.ReadEntries().GetEnumerator();

            Assert.True(entries.MoveNext());
            Assert.NotNull(entries.Current);

            Assert.Equal("file1.txt", entries.Current.FullName);
            Assert.Equal("0100777", entries.Current.FileMode);
            Assert.Equal(new DateTimeOffset(2021, 9, 25, 18, 6, 34, TimeSpan.Zero), entries.Current.LastWriteTime);
            Assert.Equal(1162, entries.Current.Content.Length);

            Assert.True(entries.MoveNext());
            Assert.NotNull(entries.Current);

            Assert.Equal("file2.txt", entries.Current.FullName);
            Assert.Equal("0100777", entries.Current.FileMode);
            Assert.Equal(new DateTimeOffset(2021, 9, 25, 18, 6, 46, TimeSpan.Zero), entries.Current.LastWriteTime);
            Assert.Equal(1621, entries.Current.Content.Length);

            Assert.False(entries.MoveNext());
        }
    }
}
