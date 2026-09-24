using System.Text;
using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class StreamPrefixHelperTests
{
    [Fact]
    public void StartsWith_AccumulatesRepeatedShortReads()
    {
        var prefix = Encoding.ASCII.GetBytes("R2KENC02");
        var payload = prefix.Concat([0x01, 0x02, 0x03]).ToArray();
        using var stream = new ShortReadStream(payload, maximumReadSize: 2);

        Assert.True(StreamPrefixHelper.StartsWith(stream, prefix));
        Assert.Equal(prefix.Length, stream.TotalBytesRead);
    }

    [Fact]
    public void StartsWith_TruncatedPrefix_ReturnsFalse()
    {
        var prefix = Encoding.ASCII.GetBytes("R2KENC02");
        using var stream = new ShortReadStream(Encoding.ASCII.GetBytes("R2KEN"), maximumReadSize: 2);

        Assert.False(StreamPrefixHelper.StartsWith(stream, prefix));
    }

    [Fact]
    public void StartsWith_DifferentPrefix_ReturnsFalse()
    {
        var prefix = Encoding.ASCII.GetBytes("R2KENC02");
        using var stream = new ShortReadStream(Encoding.ASCII.GetBytes("R2KENC01payload"), maximumReadSize: 3);

        Assert.False(StreamPrefixHelper.StartsWith(stream, prefix));
    }

    private sealed class ShortReadStream : Stream
    {
        private readonly MemoryStream _inner;
        private readonly int _maximumReadSize;

        public ShortReadStream(byte[] payload, int maximumReadSize)
        {
            _inner = new MemoryStream(payload, writable: false);
            _maximumReadSize = maximumReadSize;
        }

        public int TotalBytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, Math.Min(count, _maximumReadSize));
            TotalBytesRead += read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = _inner.Read(buffer[..Math.Min(buffer.Length, _maximumReadSize)]);
            TotalBytesRead += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
