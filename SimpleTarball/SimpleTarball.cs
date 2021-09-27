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
            while (ReadEntry(out TarballEntry entry))
            {
                if (entry == null)
                {
                    continue;
                }

                long nextEntryPosition = BaseStream.Position + (long)Math.Ceiling(entry.Length / 512d) * 512;

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

        private bool ReadEntry(out TarballEntry entry)
        {
            byte[] buffer = new byte[512];
            int read = BaseStream.Read(buffer, 0, buffer.Length);

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

            entry = new TarballEntry(
                BaseStream,
                BaseStream.Position - 512,
                Encoding.UTF8.GetString(buffer, 0, 100).TrimEnd('\0'),
                DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(Encoding.ASCII.GetString(buffer, 136, 11), 8)),
                Convert.ToInt64(Encoding.ASCII.GetString(buffer, 124, 11), 8)
            );

            return true;
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

        public TarballEntry CreateEntry(string fullName)
        {
            return new TarballEntry(BaseStream, BaseStream.Position, fullName, DateTimeOffset.UtcNow, 0);
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
        private readonly Stream baseStream;
        private readonly long startPosition;
        private readonly long contentStartPosition;

        internal TarballEntry(Stream baseStream, long startPosition, string fullName, DateTimeOffset lastWriteTime, long length)
        {
            this.baseStream = baseStream;
            this.startPosition = startPosition;
            this.contentStartPosition = startPosition + 512;

            this.FullName = fullName;
            this.LastWriteTime = lastWriteTime;
            this.Length = length;
        }

        public string FullName { get; }

        public DateTimeOffset LastWriteTime { get; private set; }

        public long Length { get; private set; }

        public Stream OpenRead()
        {
            return new WrappedStream(this, contentStartPosition, Length, false);
        }

        public Stream OpenWrite()
        {
            return OpenWrite(default(DateTimeOffset));
        }

        public Stream OpenWrite(DateTimeOffset lastWriteTime)
        {
            return new BufferedWriteStream(this, lastWriteTime);
        }

        public Stream OpenWrite(long length)
        {
            return OpenWrite(default, length);
        }

        public Stream OpenWrite(DateTimeOffset lastWriteTime, long length)
        {
            LastWriteTime = !lastWriteTime.Equals(default) ? lastWriteTime : DateTimeOffset.UtcNow;
            Length = length;

            EnsureStart();
            WriteHeader();

            return new WrappedStream(this, contentStartPosition, length, true);
        }

        private void EnsureStart()
        {
            if (baseStream.Position != startPosition)
            {
                throw new InvalidOperationException($"Stream is not at expected entry start position (expected {startPosition}, got {baseStream.Position})");
            }
        }

        private void EnsureContentStart()
        {
            if (baseStream.Position != contentStartPosition)
            {
                throw new InvalidOperationException($"Stream is not at expected content start position (expected {contentStartPosition}, got {baseStream.Position})");
            }
        }

        private void EnsureEnd()
        {
            long contentEndPosition = contentStartPosition + Length;

            if (baseStream.Position < contentEndPosition)
            {
                baseStream.Position = contentEndPosition;
            }
            else if (baseStream.Position > contentEndPosition)
            {
                throw new InvalidOperationException($"Stream is past expected end position (expected {contentEndPosition}, got {baseStream.Position})");
            }
        }

        private void WriteHeader()
        {
            byte[] buffer = new byte[512];

            WriteString(FullName, Encoding.UTF8, 100, buffer, 0); // name
            WriteString("0100777", 7, buffer, 100); // mode
            WriteInt32AsOctal(0, buffer, 108); // uid
            WriteInt32AsOctal(0, buffer, 116); // gid
            WriteInt64AsOctal(Length, buffer, 124); // size
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

            baseStream.Write(buffer, 0, buffer.Length);
        }

        private void WritePadding()
        {
            EnsureEnd();

            if (Length % 512 != 0)
            {
                byte[] padding = new byte[512 - (Length % 512)];
                baseStream.Write(padding, 0, padding.Length);
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
            private readonly TarballEntry entry;
            private readonly long startPosition;

            private bool isOpen = true;

            public WrappedStream(TarballEntry entry, long startPosition, long length, bool write)
            {
                entry.EnsureContentStart();

                CanRead = !write;
                CanWrite = write;

                this.entry = entry;
                this.startPosition = startPosition;

                Length = length;
            }

            public override bool CanRead { get; }

            public override bool CanSeek => false;

            public override bool CanWrite { get; }

            public override long Length { get; }

            public override long Position
            {
                get => entry.baseStream.Position - startPosition;
                set => entry.baseStream.Position = startPosition + value;
            }

            public override void Flush()
            {
                throw new NotImplementedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (!CanRead)
                {
                    throw new InvalidOperationException("Stream is write-only");
                }

                if (!isOpen)
                {
                    throw new InvalidOperationException("Cannot read from a closed stream");
                }

                if (count < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(count));
                }

                count = (int)Math.Min(count, Length - Position);
                return entry.baseStream.Read(buffer, offset, count);
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
                if (!CanWrite)
                {
                    throw new InvalidOperationException("Stream is read-only");
                }

                if (!isOpen)
                {
                    throw new InvalidOperationException("Cannot write to a closed stream");
                }

                if (Position + count > Length)
                {
                    throw new InvalidOperationException("Attempted to write outside stream bounds");
                }

                entry.baseStream.Write(buffer, offset, count);
            }

            public override void Close()
            {
                if (isOpen)
                {
                    if (CanWrite)
                    {
                        entry.WritePadding();
                    }

                    isOpen = false;
                }

                base.Close();
            }
        }

        private class BufferedWriteStream : MemoryStream
        {
            private readonly TarballEntry entry;
            private readonly DateTimeOffset lastWriteTime;

            private bool isOpen = true;

            public BufferedWriteStream(TarballEntry entry, DateTimeOffset lastWriteTime)
            {
                this.entry = entry;
                this.lastWriteTime = lastWriteTime;
            }

            public override void Close()
            {
                if (isOpen)
                {
                    entry.EnsureStart();
                    entry.LastWriteTime = !lastWriteTime.Equals(default) ? lastWriteTime : DateTimeOffset.UtcNow;
                    entry.Length = Length;
                    entry.WriteHeader();

                    Position = 0;
                    CopyTo(entry.baseStream);
                    entry.WritePadding();

                    isOpen = false;
                }

                base.Close();
            }
        }
    }
}
