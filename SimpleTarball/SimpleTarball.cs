using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SimpleTarball
{
    public class TarballReader : IDisposable
    {
        private readonly bool leaveOpen;

        public TarballReader(Stream stream, bool leaveOpen = false)
        {
            if (!stream.CanRead)
            {
                throw new InvalidOperationException("Cannot read from stream");
            }

            BaseStream = stream;
            this.leaveOpen = leaveOpen;
        }

        public TarballReader(string path)
        {
            BaseStream = new FileStream(path, FileMode.Open, FileAccess.Read);
            this.leaveOpen = false;
        }

        public Stream BaseStream { get; }

        public IEnumerable<TarballEntry> ReadEntries()
        {
            while (TarballEntry.ReadFrom(BaseStream, out TarballEntry entry))
            {
                if (entry == null)
                {
                    continue;
                }

                long nextEntryPosition = BaseStream.Position + (long)Math.Ceiling(entry.Content.Length / 512d) * 512;

                yield return entry;

                BaseStream.Position = nextEntryPosition;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing && !leaveOpen)
            {
                BaseStream.Dispose();
            }
        }
    }

    public class TarballWriter : IDisposable
    {
        private readonly bool leaveOpen;

        public TarballWriter(Stream stream, bool leaveOpen = false)
        {
            if (!stream.CanWrite)
            {
                throw new InvalidOperationException("Cannot write to stream");
            }

            BaseStream = stream;
            this.leaveOpen = leaveOpen;
        }

        public TarballWriter(string path)
        {
            BaseStream = new FileStream(path, FileMode.Create, FileAccess.Write);
            this.leaveOpen = false;
        }

        public Stream BaseStream { get; }

        public void WriteEntry(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException(nameof(path));
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("File not found", path);
            }

            FileInfo fileInfo = new FileInfo(path);

            using (FileStream fileStream = fileInfo.OpenRead())
            {
                new TarballEntry { FullName = path, Content = fileStream, LastWriteTime = fileInfo.LastWriteTimeUtc }.WriteTo(BaseStream);
            }
        }

        public void WriteEntry(string fullName, Stream stream)
        {
            if (string.IsNullOrEmpty(fullName))
            {
                throw new ArgumentNullException(nameof(TarballEntry.Content));
            }

            if (stream == null)
            {
                throw new ArgumentNullException(nameof(TarballEntry.Content));
            }

            if (!stream.CanRead)
            {
                throw new InvalidOperationException("Cannot read from stream");
            }

            new TarballEntry { FullName = fullName, Content = stream }.WriteTo(BaseStream);
        }

        public void WriteEntry(TarballEntry entry)
        {
            if (string.IsNullOrEmpty(entry.FullName))
            {
                throw new ArgumentNullException(nameof(TarballEntry.Content));
            }

            if (entry.Content == null)
            {
                throw new ArgumentNullException(nameof(TarballEntry.Content));
            }

            entry.WriteTo(BaseStream);
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                WriteFooter();

                if (!leaveOpen)
                {
                    BaseStream.Dispose();
                }
            }
        }

        private void WriteFooter()
        {
            byte[] buffer = new byte[1024];
            BaseStream.Write(buffer, 0, buffer.Length);
        }
    }

    // https://www.gnu.org/software/tar/manual/html_node/Standard.html
    public class TarballEntry
    {
        public string FullName { get; set; }

        public string FileMode { get; set; } = "0100777";

        public DateTimeOffset LastWriteTime { get; set; } = DateTimeOffset.UtcNow;

        public Stream Content { get; set; }

        public static bool ReadFrom(Stream stream, out TarballEntry entry)
        {
            byte[] buffer = new byte[512];
            int read = stream.Read(buffer, 0, buffer.Length);

            if (read != buffer.Length)
            {
                entry = null;
                return false;
            }

            if (buffer.All(b => b == 0))
            {
                entry = null;
                return true;
            }

            entry = new TarballEntry
            {
                FullName = Encoding.UTF8.GetString(buffer, 0, 100).TrimEnd('\0'),
                FileMode = Encoding.UTF8.GetString(buffer, 100, 7).TrimEnd('\0'),
                LastWriteTime = DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(Encoding.ASCII.GetString(buffer, 136, 11), 8)),
                Content = new WrappedStream(stream, stream.Position, Convert.ToInt64(Encoding.ASCII.GetString(buffer, 124, 11), 8)),
            };

            return true;
        }

        public void WriteTo(Stream stream)
        {
            byte[] buffer = new byte[512];

            WriteString(FullName, Encoding.UTF8, 100, buffer, 0); // name
            WriteString(FileMode, 7, buffer, 100); // mode
            WriteInt32AsOctal(0, buffer, 108); // uid
            WriteInt32AsOctal(0, buffer, 116); // gid
            WriteInt64AsOctal(Content.Length, buffer, 124); // size
            WriteInt64AsOctal(LastWriteTime.ToUnixTimeSeconds(), buffer, 136); // mtime
            WriteString(new string(' ', 8), 8, buffer, 148); // checksum (calculated below)
            WriteString("ustar\000", 8, buffer, 257); // magic + version
            buffer[156] = 0x30; // typeFlag '0'

            long checksum = 0;

            unchecked
            {
                foreach (byte b in buffer)
                {
                    checksum += b;
                }
            }

            // checksum (calculated)
            Encoding.UTF8.GetBytes(Convert.ToString(checksum, 8).PadLeft(6, '0').Substring(0, 6) + "\0 ", 0, 7, buffer, 148);

            stream.Write(buffer, 0, buffer.Length);

            Content.CopyTo(stream);

            // pad last block with NULs
            if (Content.Length % 512 != 0)
            {
                byte[] padding = new byte[512 - Content.Length % 512];
                stream.Write(padding, 0, padding.Length);
            }
        }

        private int WriteString(string str, int maxLength, byte[] buffer, int index)
        {
            return WriteString(str, Encoding.ASCII, maxLength, buffer, index);
        }

        private int WriteString(string str, Encoding encoding, int maxLength, byte[] buffer, int index)
        {
            if (str.Length > maxLength)
            {
                throw new ArgumentOutOfRangeException(nameof(str));
            }

            return Encoding.ASCII.GetBytes(str, 0, str.Length, buffer, index);
        }

        private int WriteInt32AsOctal(int num, byte[] buffer, int index)
        {
            if (num < 0 || num > 2097151) // 8^7 - 1
            {
                throw new ArgumentOutOfRangeException(nameof(num));
            }

            return Encoding.UTF8.GetBytes(Convert.ToString(num, 8).PadLeft(7, '0').Substring(0, 7), 0, 7, buffer, index);
        }

        private int WriteInt64AsOctal(long num, byte[] buffer, int index)
        {
            if (num < 0 || num > 8589934591) // 8^11 - 1
            {
                throw new ArgumentOutOfRangeException(nameof(num));
            }

            return Encoding.UTF8.GetBytes(Convert.ToString(num, 8).PadLeft(11, '0').Substring(0, 11), 0, 11, buffer, index);
        }

        private class WrappedStream : Stream
        {
            private readonly Stream baseStream;
            private readonly long startPosition;

            public WrappedStream(Stream baseStream, long startPosition, long length)
            {
                this.baseStream = baseStream;
                this.startPosition = startPosition;

                Length = length;
            }

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length { get; }

            public override long Position
            {
                get => baseStream.Position - startPosition;
                set => baseStream.Position = startPosition + value;
            }

            public override void Flush()
            {
                throw new NotImplementedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (count < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(count));
                }

                count = (int)Math.Min(count, Length - Position);
                return baseStream.Read(buffer, offset, count);
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }
        }
    }
}
