using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace SimpleTarball.Tests
{
    public class TarballWriterTests
    {
        private class TestOutputTextWriter : TextWriter
        {
            private readonly ITestOutputHelper testOutputHelper;

            public TestOutputTextWriter(ITestOutputHelper testOutputHelper)
            {
                this.testOutputHelper = testOutputHelper;
            }

            public override Encoding Encoding => Encoding.UTF8;

            public override void WriteLine(string value)
            {
                testOutputHelper.WriteLine(value);
            }

            public override void WriteLine(string format, params object[] args)
            {
                testOutputHelper.WriteLine(format, args);
            }

            public override void Write(string value)
            {
                throw new NotSupportedException();
            }
        }

        public TarballWriterTests(ITestOutputHelper testOutputHelper)
        {
            Console.SetOut(new TestOutputTextWriter(testOutputHelper));
        }

        [Fact]
        public void WriteTarball()
        {
            using MemoryStream memoryStream = new();

            using (TarballWriter writer = new(memoryStream))
            {
                TarballEntry entry1 = writer.CreateEntry("file1.txt");

                using (FileStream fileStream = new("Resources/file1.txt", FileMode.Open, FileAccess.Read))
                using (Stream stream = entry1.OpenWrite(new DateTimeOffset(2021, 9, 25, 18, 6, 34, TimeSpan.Zero)))
                {
                    fileStream.CopyTo(stream);
                }

                TarballEntry entry2 = writer.CreateEntry("file2.txt");

                using (FileStream fileStream = new("Resources/file2.txt", FileMode.Open, FileAccess.Read))
                using (Stream stream = entry2.OpenWrite(new DateTimeOffset(2021, 9, 25, 18, 6, 46, TimeSpan.Zero)))
                {
                    fileStream.CopyTo(stream);
                }
            }

            Assert.Equal(File.ReadAllText("Resources/SampleTarball.tar", Encoding.ASCII), Encoding.ASCII.GetString(memoryStream.ToArray()));
        }

        [Fact]
        public void WriteTarballWithPredeterminedLengths()
        {
            using MemoryStream memoryStream = new();

            using (TarballWriter writer = new(memoryStream))
            {
                TarballEntry entry1 = writer.CreateEntry("file1.txt");

                using (FileStream fileStream = new("Resources/file1.txt", FileMode.Open, FileAccess.Read))
                using (Stream stream = entry1.OpenWrite(new DateTimeOffset(2021, 9, 25, 18, 6, 34, TimeSpan.Zero), fileStream.Length))
                {
                    fileStream.CopyTo(stream);
                }

                TarballEntry entry2 = writer.CreateEntry("file2.txt");

                using (FileStream fileStream = new("Resources/file2.txt", FileMode.Open, FileAccess.Read))
                using (Stream stream = entry2.OpenWrite(new DateTimeOffset(2021, 9, 25, 18, 6, 46, TimeSpan.Zero), fileStream.Length))
                {
                    fileStream.CopyTo(stream);
                }
            }

            Assert.Equal(File.ReadAllText("Resources/SampleTarball.tar", Encoding.ASCII), Encoding.ASCII.GetString(memoryStream.ToArray()));
        }

        [Fact]
        public void WriteTarballWithGzippedEntry()
        {
            using MemoryStream memoryStream = new();

            using (TarballWriter writer = new(memoryStream))
            {
                TarballEntry entry1 = writer.CreateEntry("file1.txt.gz");

                using (FileStream fileStream = new("Resources/file1.txt", FileMode.Open, FileAccess.Read))
                using (Stream stream = entry1.OpenWrite(new DateTimeOffset(2021, 9, 25, 18, 6, 34, TimeSpan.Zero)))
                using (GZipStream gzipStream = new(stream, CompressionLevel.Optimal))
                {
                    fileStream.CopyTo(gzipStream);
                }
            }

            Assert.NotNull(memoryStream);
        }
    }
}
