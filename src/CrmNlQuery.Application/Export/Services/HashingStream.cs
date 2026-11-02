using System.Security.Cryptography;

namespace CrmNlQuery.Application.Export.Services;

/// <summary>
/// Wraps an output stream and computes the SHA-256 and length of the bytes actually sent.
/// </summary>
internal sealed class HashingStream : Stream
{
    private readonly Stream _inner;
    private readonly IncrementalHash _hash;

    public HashingStream(Stream inner, IncrementalHash hash)
    {
        _inner = inner;
        _hash = hash;
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => Written;
    public override long Position { get => Written; set => throw new NotSupportedException(); }
    public long Written { get; private set; }

    /// <summary>
    /// Flushes the wrapped output stream so the hashing stream passes buffered bytes through synchronously.
    /// </summary>
    public override void Flush() => _inner.Flush();
    /// <summary>
    /// Flushes the wrapped output stream asynchronously so the hashing stream passes buffered bytes through.
    /// </summary>
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
    /// <summary>
    /// Is not supported because the stream is write-only.
    /// </summary>
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    /// <summary>
    /// Is not supported because the stream is write-only.
    /// </summary>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    /// <summary>
    /// Is not supported because the stream is write-only.
    /// </summary>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <summary>
    /// Writes bytes to the wrapped stream and adds them to the running hash and length.
    /// </summary>
    public override void Write(byte[] buffer, int offset, int count)
    {
        _inner.Write(buffer, offset, count);
        _hash.AppendData(buffer, offset, count);
        Written += count;
    }

    /// <summary>
    /// Writes bytes to the wrapped stream asynchronously and adds them to the running hash and length.
    /// </summary>
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await _inner.WriteAsync(buffer, cancellationToken);
        _hash.AppendData(buffer.Span);
        Written += buffer.Length;
    }
}
