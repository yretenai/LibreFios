using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LibreFios.Compression;

[SuppressMessage("ReSharper", "PartialTypeWithSinglePart")]
internal static partial class Oodle {
	static Oodle() {
		BlockDecoderMemorySizeNeeded = NativeMethods.OodleLZDecoder_MemorySizeNeeded(OodleLZ_Compressor.Invalid, -1);
	}

	internal static int BlockDecoderMemorySizeNeeded { get; }

	internal static string ParseOodleVersion(uint value) {
		var check = value >> 28;
		var provider = (value >> 24) & 0xF;
		var major = (value >> 16) & 0xFF;
		var minor = (value >> 8) & 0xFF;
		var table = value & 0xFF;
		return $"{check}.{major}.{minor} (provider: {provider:X1}, seek: {table})";
	}

	internal static uint CreateOodleVersion(int major, int minor, int seekTableSize = 48) => (46u << 24) | (uint) (major << 16) | (uint) (minor << 8) | (uint) seekTableSize;

	internal static unsafe int Decompress(Memory<byte> input, Memory<byte> output) {
		using var inPin = input.Pin();
		using var outPin = output.Pin();
		using var pool = MemoryPool<byte>.Shared.Rent(BlockDecoderMemorySizeNeeded);
		using var poolPin = pool.Memory.Pin();
		return NativeMethods.OodleLZ_Decompress((byte*) inPin.Pointer, input.Length, (byte*) outPin.Pointer, output.Length, true, false, OodleLZ_Verbosity.Minimal, null, 0, null, null, (byte*) poolPin.Pointer, BlockDecoderMemorySizeNeeded, OodleLZ_Decode_ThreadPhase.Unthreaded);
	}

	internal static unsafe OodleLZ_Compressor GetCompressor(Memory<byte> input) {
		using var inPin = input.Pin();
		var independent = false;
		return NativeMethods.OodleLZ_GetFirstChunkCompressor((byte*) inPin.Pointer, input.Length, ref independent);
	}

	internal static string GetCompressorName(Memory<byte> input) {
		var compressor = GetCompressor(input);
		return GetCompressorName(compressor);
	}

	internal static string GetCompressorName(OodleLZ_Compressor compressor) => NativeMethods.OodleLZ_Compressor_GetName(compressor);

	internal enum OodleLZ_Compressor {
		Invalid = -1,
		LZH = 0,
		LZHLW = 1,
		LZNIB = 2,
		None = 3,
		LZB16 = 4,
		LZBLW = 5,
		LZA = 6,
		LZNA = 7,
		Kraken = 8,
		Mermaid = 9,
		BitKnit = 10,
		Selkie = 11,
		Hydra = 12,
		Leviathan = 13,
	}

	internal enum OodleLZ_Decode_ThreadPhase {
		ThreadPhase1 = 1,
		ThreadPhase2 = 2,
		ThreadPhaseAll = 3,
		Unthreaded = ThreadPhaseAll,
	}

	internal enum OodleLZ_Verbosity {
		None = 0,
		Minimal = 1,
		Some = 2,
		Lots = 3,
	}

	private static partial class NativeMethods {
	#if !NET8_0_OR_GREATER
		[DllImport("oo2core"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		internal static extern unsafe int OodleLZ_Decompress(byte* srcBuf, long srcSize, byte* rawBuf, long rawSize, [MarshalAs(UnmanagedType.I4)] bool fuzzSafe, [MarshalAs(UnmanagedType.I4)] bool checkCRC, OodleLZ_Verbosity verbosity, byte* decBufBase, long decBufSize, void* fpCallback, void* callbackUserData, byte* decoderMemory, long decoderMemorySize, OodleLZ_Decode_ThreadPhase threadPhase);

		[DllImport("oo2core"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		internal static extern unsafe OodleLZ_Compressor OodleLZ_GetFirstChunkCompressor(byte* srcBuf, long srcSize, [MarshalAs(UnmanagedType.I4)] ref bool independent);

		[DllImport("oo2core", CharSet = CharSet.Ansi), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		[return: MarshalAs(UnmanagedType.LPStr)]
		internal static extern string OodleLZ_Compressor_GetName(OodleLZ_Compressor compressor);

		[DllImport("oo2core"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		internal static extern int OodleLZDecoder_MemorySizeNeeded(OodleLZ_Compressor compressor, long size);
	#else
		[LibraryImport("oo2core"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		internal static unsafe partial int OodleLZ_Decompress(byte* srcBuf, long srcSize, byte* rawBuf, long rawSize, [MarshalAs(UnmanagedType.I4)] bool fuzzSafe, [MarshalAs(UnmanagedType.I4)] bool checkCRC, OodleLZ_Verbosity verbosity, byte* decBufBase, long decBufSize, void* fpCallback, void* callbackUserData, byte* decoderMemory, long decoderMemorySize, OodleLZ_Decode_ThreadPhase threadPhase);

		[LibraryImport("oo2core"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		internal static unsafe partial OodleLZ_Compressor OodleLZ_GetFirstChunkCompressor(byte* srcBuf, long srcSize, [MarshalAs(UnmanagedType.I4)] ref bool independent);

		[LibraryImport("oo2core", StringMarshalling = StringMarshalling.Utf8), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		[return: MarshalAs(UnmanagedType.LPStr)]
		internal static partial string OodleLZ_Compressor_GetName(OodleLZ_Compressor compressor);

		[LibraryImport("oo2core"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		internal static partial int OodleLZDecoder_MemorySizeNeeded(OodleLZ_Compressor compressor, long size);
	#endif
	}
}
