using System.Buffers;

namespace LibreFios;

public interface IPSARCBuffer : IDisposable {
	public static NullBuffer Empty { get; } = new();

	public int Length { get; }
	public ReadOnlySpan<byte> Data { get; }
	public byte this[int offset] { get; }
}

public sealed class NullBuffer : IPSARCBuffer {
	public int Length => 0;
	public ReadOnlySpan<byte> Data => ReadOnlySpan<byte>.Empty;
	public byte this[int offset] => 0;

	public void Dispose() { }
}

public sealed class PSARCMemoryBuffer(IMemoryOwner<byte> Buffer, int Size) : IPSARCBuffer {
	public Span<byte> WritableData => Buffer.Memory.Span[..Size];
	public int Length => Size;
	public ReadOnlySpan<byte> Data => WritableData;
	public byte this[int offset] => Data[offset];

	public void Dispose() {
		Buffer.Dispose();
	}
}
