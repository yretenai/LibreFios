using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using LibreFios.Compression;
using LibreFios.Structures;
using LZMADecoder = SevenZip.Compression.LZMA.Decoder;

namespace LibreFios;

#if !NET8_0_OR_GREATER
internal static class StreamPolyfillExtensions {
	internal static void ReadExactly(this Stream stream, Span<byte> buffer) {
		var minimumBytes = buffer.Length;
		var totalRead = 0;
		while (totalRead < minimumBytes) {
			var read = stream.Read(buffer[totalRead..]);
			if (read == 0) {
				return;
			}

			totalRead += read;
		}
	}
}
#endif

public sealed class PSARC : IDisposable {
	public PSARC(Stream stream) {
		BaseStream = stream;

		if (stream.Length < 0x20) {
			FileEntries = [];
			Manifest = [];
			BlockSizeBuffer = MemoryPool<byte>.Shared.Rent(0);
			return;
		}

		Span<PSARCHeader> header = stackalloc PSARCHeader[1];
		BaseStream.ReadExactly(MemoryMarshal.AsBytes(header));
		Header = header[0];

		Manifest = new Dictionary<string, PSARCHash>(Header.ArchiveFlags.HasFlagFast(PSARCArchiveFlags.CaseInsensitivePaths) ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

		// check if the file starts with "PSAR"
		if (Header.Magic != PSARCHeader.PSAR) {
			throw new InvalidDataException("Not a PSARC File");
		}

		// we only support version 1.x
		if (Header.Version > 2 || Header.Version < 1) {
			throw new NotSupportedException("PSARC has an unsupported version");
		}

		// some sanity checks to ensure things are still valid
		if (Header.FAT.EntrySize < 0x1e || Header.BlockSize < 0x100) {
			throw new InvalidDataException("PSARC File is corrupt");
		}

		// read entries in one go
		var entries = MemoryPool<PSARCFileEntry>.Shared.Rent(Header.FAT.Count);
		var entriesSpan = entries.Memory.Span[..Header.FAT.Count];
		var entriesBytes = MemoryMarshal.AsBytes(entriesSpan);
		BaseStream.ReadExactly(entriesBytes);

		// amortize it into a dictionary.
		FileEntries = new Dictionary<PSARCHash, PSARCFileEntry>(Header.FAT.Count);
		foreach (var entry in entriesSpan) {
			FileEntries[entry.Hash] = entry;
		}

		// read the compression block list in one go
		var blockSizeBufferSize = Header.FAT.Size - entriesBytes.Length;
		BlockSizeBuffer = MemoryPool<byte>.Shared.Rent(blockSizeBufferSize);
		BaseStream.ReadExactly(BlockSizeBuffer.Memory.Span[..blockSizeBufferSize]);

		// read manifest (if it exists)
		// manifest has no hash.
		using var manifest = OpenFile(default(PSARCHash));
		if (manifest.Length > 0) {
			// todo: check if this always matches the file order, it might be possible to just skip md5-ing the path.
			foreach (var filePath in Encoding.ASCII.GetString(manifest.Data).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) {
				// normalize paths.
				// todo: can relative paths start with '/'?
				Manifest[filePath.Replace('\\', '/')] = new PSARCHash(Header.ArchiveFlags.HasFlagFast(PSARCArchiveFlags.CaseInsensitivePaths) ? filePath.ToUpperInvariant() : filePath);
			}
		}
	}

	// allocate 255 bytes for random slop to avoid making allocations over and over and over again.
	internal byte[] ScratchPad { get; } = ArrayPool<byte>.Shared.Rent(byte.MaxValue);

	internal PSARCHeader Header { get; set; }
	internal IMemoryOwner<byte> BlockSizeBuffer { get; set; }

	public Stream BaseStream { get; set; }
	public Dictionary<PSARCHash, PSARCFileEntry> FileEntries { get; set; }
	public Dictionary<string, PSARCHash> Manifest { get; set; }
	public IEnumerable<string> Paths => Manifest.Keys;

	public void Dispose() {
		ArrayPool<byte>.Shared.Return(ScratchPad);
		BaseStream.Dispose();
		BlockSizeBuffer.Dispose();
	}

	internal PSARCMemoryBuffer OpenFile(PSARCFileEntry file) {
		// unfortunately, Span<T> only holds 2 GB.
		// it's not impossible to refactor this support larger (i.e. a PSARCMemoryStream implementation of IPSARCBuffer)
		if (file.DecompressedSize > int.MaxValue) {
			throw new NotSupportedException("Large files are not supported.");
		}

		BaseStream.Position = file.Offset;

		// rent some data from the memory pool for reading and decompressing.
		using var rentedBlockBuffer = MemoryPool<byte>.Shared.Rent(Header.BlockSize);
		using var rentedDataBuffer = MemoryPool<byte>.Shared.Rent(Header.BlockSize);
		var blockBuffer = rentedBlockBuffer.Memory.Span[..Header.BlockSize];
		var dataBuffer = rentedDataBuffer.Memory.Span[..Header.BlockSize];
		var blockIndex = file.BlockIndex;
		var resultBuffer = new PSARCMemoryBuffer(MemoryPool<byte>.Shared.Rent((int) file.DecompressedSize), (int) file.DecompressedSize);
		var resultSpan = resultBuffer.WritableData;

		var fill = (int) file.DecompressedSize;
		while (fill > 0) {
			// read the next block
			var blockSize = GetCompressedBlockSize(blockIndex++);
			var blockSlice = blockBuffer[..(blockSize == 0 ? Header.BlockSize : blockSize)];
			BaseStream.ReadExactly(blockSlice);

			// if it has the encrypted flag, decrypt it here
			if (Header.ArchiveFlags.HasFlagFast(PSARCArchiveFlags.EncryptedFiles)) {
				// we've never seen this file or this implemented in any implementation.
				// but we've seen the flag in two places.
				var key = BlockSizeBuffer.Memory.Span[^20..^10];
				var iv = BlockSizeBuffer.Memory.Span[^10..];
				using var aes = Aes.Create();
				key.CopyTo(ScratchPad);
				aes.Key = ScratchPad[..0x10];
				// todo: maybe ECB?
				var n = aes.DecryptCbc(blockSlice, iv, rentedDataBuffer.Memory.Span);

				// copy data back into the compressed buffer
				rentedDataBuffer.Memory.Span[..blockSlice.Length].CopyTo(blockSlice);
				blockSlice = blockBuffer[..n];
			}

			// if the block is exactly the same size as the remaining buffer, it's not compressed (usually.)
			if (blockSlice.Length == fill || blockSize == 0) {
				blockSlice.CopyTo(resultSpan[(resultBuffer.Length - fill)..]);
				fill -= blockSlice.Length;
				continue;
			}

			switch (Header.CompressionType) {
				// this is not actually a valid compression type, but here for completeness
				case PSARCCompressionType.Invalid:
				case PSARCCompressionType.None: {
					blockSlice.CopyTo(resultSpan[(resultBuffer.Length - fill)..]);
					fill -= blockSlice.Length;
					break;
				}
				case PSARCCompressionType.ZLib: {
					// calculate how many bytes we'll be reading
					var n = Math.Min(fill, Header.BlockSize);

					unsafe {
						// wrap the compressed buffer span into a stream
						using var unmanagedCompressedPin = rentedBlockBuffer.Memory.Pin();
						using var unmanagedCompressed = new UnmanagedMemoryStream((byte*) unmanagedCompressedPin.Pointer, blockBuffer.Length);
						using var compressionStream = new ZLibStream(unmanagedCompressed, CompressionMode.Decompress);
						compressionStream.ReadExactly(dataBuffer[..n]);
					}

					// copy the decompressed data into the resulting buffer
					dataBuffer[..n].CopyTo(resultSpan[(resultBuffer.Length - fill)..]);
					fill -= n;

					break;
				}
				case PSARCCompressionType.LZMA: { // note: find a better lzma implementation
					var lzma = new LZMADecoder();
					var uncompressedSize = BinaryPrimitives.ReadUInt64LittleEndian(blockBuffer[5..]);
					blockBuffer[..5].CopyTo(ScratchPad);
					lzma.SetDecoderProperties(ScratchPad[..5]);
					var n = Math.Min(fill, Header.BlockSize);
					unsafe {
						using var unmanagedCompressedPin = rentedBlockBuffer.Memory.Pin();
						using var unmanagedCompressed = new UnmanagedMemoryStream((byte*) unmanagedCompressedPin.Pointer, blockBuffer.Length);
						using var unmanagedDecompressedPin = rentedDataBuffer.Memory.Pin();
						using var unmanagedDecompressed = new UnmanagedMemoryStream((byte*) unmanagedDecompressedPin.Pointer, dataBuffer.Length);
						unmanagedCompressed.Position = 13;
						lzma.Code(unmanagedCompressed, unmanagedDecompressed, unmanagedCompressed.Length - 13, unmanagedDecompressed.Length, null);
					}

					if ((ulong) n > uncompressedSize) { // this should NEVER trigger.
						n = (int) uncompressedSize;
					}

					dataBuffer[..n].CopyTo(resultSpan[(resultBuffer.Length - fill)..]);
					fill -= n;
					break;
				}
				case PSARCCompressionType.Oodle: { // note: provide oo2core.dll (rename it)
					var n = Oodle.Decompress(rentedBlockBuffer.Memory[..Header.BlockSize], rentedDataBuffer.Memory[..Header.BlockSize]);
					dataBuffer[..n].CopyTo(resultSpan[(resultBuffer.Length - fill)..]);
					fill -= n;
					break;
				}
				case PSARCCompressionType.ZStandard: { // note: provide libzstd.dll
					using var zstd = new ZStandard();
					var n = (int) zstd.Decompress(rentedBlockBuffer.Memory[..Header.BlockSize], rentedDataBuffer.Memory[..Header.BlockSize]);
					dataBuffer[..n].CopyTo(resultSpan[(resultBuffer.Length - fill)..]);
					fill -= n;
					break;
				}
				default: throw new NotSupportedException("Unknown compression method");
			}
		}

		return resultBuffer;
	}

	public IPSARCBuffer OpenFile(PSARCHash hash) => FileEntries.TryGetValue(hash, out var file) ? OpenFile(file) : IPSARCBuffer.Empty;
	public IPSARCBuffer OpenFile(string path) => Manifest.TryGetValue(path.Replace('\\', '/'), out var hash) ? OpenFile(hash) : OpenFile(new PSARCHash(Header.ArchiveFlags.HasFlagFast(PSARCArchiveFlags.CaseInsensitivePaths) ? path.ToUpperInvariant() : path));

	public Dictionary<PSARCHash, string> BuildReversePaths() => Manifest.ToDictionary(x => x.Value, x => x.Key);

	private int GetCompressedBlockSize(int index) {
		var offset = Header.CompressedBlockSize * index;
		var bufferSpan = BlockSizeBuffer.Memory.Span;
		return Header.CompressedBlockSize switch {
			       0 => 0,
			       1 => bufferSpan[offset],
			       2 => (bufferSpan[offset++] << 8) | bufferSpan[offset],
			       3 => (bufferSpan[offset++] << 16) | (bufferSpan[offset++] << 8) | bufferSpan[offset],
			       4 => (bufferSpan[offset++] << 24) | (bufferSpan[offset++] << 16) | (bufferSpan[offset++] << 8) | bufferSpan[offset],
			       _ => 0,
		       };
	}
}
