using System;
using System.IO;
using Xunit;

namespace SimpleTarball.Tests
{
    public class TarballWriterTests
    {
        [Fact]
        public void WriteTarball()
        {
            using MemoryStream memoryStream = new();

            using (TarballWriter writer = new(memoryStream))
            {
                using (MemoryStream stream = new(File.ReadAllBytes("Resources/file1.txt")))
                {
                    writer.WriteEntry(new TarballEntry()
                    {
                        FullName = "file1.txt",
                        LastWriteTime = new DateTimeOffset(2021, 9, 25, 18, 6, 34, TimeSpan.Zero),
                        Content = stream,
                    });
                }

                using (MemoryStream stream = new(File.ReadAllBytes("Resources/file2.txt")))
                {
                    writer.WriteEntry(new TarballEntry()
                    {
                        FullName = "file2.txt",
                        LastWriteTime = new DateTimeOffset(2021, 9, 25, 18, 6, 46, TimeSpan.Zero),
                        Content = stream,
                    });
                }
            }

            Assert.Equal(File.ReadAllBytes("Resources/SampleTarball.tar"), memoryStream.ToArray());
        }
    }
}
