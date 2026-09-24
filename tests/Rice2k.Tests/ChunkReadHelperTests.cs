using Rice2k.Encryption.Services;

namespace Rice2k.Tests;

public sealed class ChunkReadHelperTests
{
    [Fact]
    public async Task ReadFullChunkAsync_FillsChunksAcrossRepeatedShortReads()
    {
        var payload = Enumerable.Range(0, 70).Select(value => (byte)value).ToArray();
        await using var input = new ShortReadStream(payload, maximumReadSize: 7);
        var buffer = new byte[32];

        var first = await ChunkReadHelper.ReadFullChunkAsync(input, buffer);
        Assert.Equal(32, first);
        Assert.Equal(payload[..32], buffer);

        var second = await ChunkReadHelper.ReadFullChunkAsync(input, buffer);
        Assert.Equal(32, second);
        Assert.Equal(payload[32..64], buffer);

        var third = await ChunkReadHelper.ReadFullChunkAsync(input, buffer);
        Assert.Equal(6, third);
        Assert.Equal(payload[64..], buffer[..third]);

        var eof = await ChunkReadHelper.ReadFullChunkAsync(input, buffer);
        Assert.Equal(0, eof);
    }

    [Fact]
    public async Task ReadFullChunkAsync_PreCancelledToken_StopsBeforeReading()
    {
        await using var input = new ShortReadStream([1, 2, 3, 4], maximumReadSize: 1);
        var buffer = new byte[4];
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await ChunkReadHelper.ReadFullChunkAsync(input, buffer, cts.Token));

        Assert.Equal(0, input.TotalBytesRead);
    }

    private sealed class ShortReadStream : Stream
    {
        private readonly MemoryStream _inner;
        private readonly int _maximumReadSize;

        public ShortReadStream(byte[] payload, int maximumReadSize)
        {
            if (maximumReadSize < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumReadSize));

            _inner = new MemoryStream(payload, writable: false);
            _maximumReadSize = maximumReadSize;
        }

        public int TotalBytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, Math.Min(count, _maximumReadSize));
            TotalBytesRead += read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var request = buffer[..Math.Min(buffer.Length, _maximumReadSize)];
            var read = await _inner.ReadAsync(request, cancellationToken);
            TotalBytesRead += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync() => _inner.DisposeAsync();
    }
}
