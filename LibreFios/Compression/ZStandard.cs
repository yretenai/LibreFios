using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LibreFios.Compression;

internal enum ZSTDParameter {
	WindowLogMax = 100,
	Format = 1000,
	StableOutBuffer = 1001,
	ForceIgnoreChecksum = 1002,
	RefMultipleDDicts = 1003,
}

internal enum ZSTDFormat {
	Normal = 0,
	Magicless = 1,
}

internal enum ZSTDDictLoadMethod {
	ByCopy = 0,
	ByRef = 1,
}

internal enum ZSTDDictContentType {
	Auto = 0,
	RawContent = 1,
	Full = 2,
}

internal enum ZSTDForceIgnoreChecksum {
	ValidateChecksum = 0,
	IgnoreChecksum = 1,
}

internal enum ZSTDRefMultipleDDicts {
	SingleDDict = 0,
	MultipleDDicts = 1,
}

[SuppressMessage("ReSharper", "PartialTypeWithSinglePart")]
internal sealed partial class ZStandard : IDisposable {
	// "Why not use the other .NET ZStd wrappers?"
	// Heap allocs. A lot of them. Heap is slow. We want fast.
	internal ZStandard() {
		DContext = NativeMethods.ZSTD_createDCtx();
		if (DContext < 0) {
			throw new OutOfMemoryException();
		}
	}

	private Memory<byte> Dict { get; set; } = Memory<byte>.Empty;
	private nint DContext { get; set; }
	private MemoryHandle DictPin { get; set; }

	public void Dispose() {
		ReleaseUnmanagedResources();
		GC.SuppressFinalize(this);
	}

	internal bool SetParameter(ZSTDParameter parameter, int value) {
		var result = NativeMethods.ZSTD_DCtx_setParameter(DContext, parameter, value);
		return result == 0;
	}

	internal unsafe bool LoadDict(Memory<byte> dict, ZSTDDictLoadMethod loadMethod = ZSTDDictLoadMethod.ByRef, ZSTDDictContentType contentType = ZSTDDictContentType.Auto) {
		if (dict.IsEmpty) {
			return UnloadDict();
		}

		Dict = dict;
		DictPin = Dict.Pin();
		var result = NativeMethods.ZSTD_DCtx_loadDictionary_advanced(DContext, (byte*) DictPin.Pointer, (nuint) Dict.Length, loadMethod, contentType);
		if (result < 0) {
			FreeDict();
			return false;
		}

		if (loadMethod != ZSTDDictLoadMethod.ByRef) {
			FreeDict();
		}

		return true;
	}

	internal unsafe long Decompress(Memory<byte> input, Memory<byte> output) {
		using var inPin = input.Pin();
		using var outPin = output.Pin();
		return NativeMethods.ZSTD_decompressDCtx(DContext, (byte*) outPin.Pointer, output.Length, (byte*) inPin.Pointer, input.Length);
	}

	internal unsafe bool UnloadDict() {
		var result = NativeMethods.ZSTD_DCtx_loadDictionary_advanced(DContext, (byte*) 0, 0, 0, 0);
		if ((nint) DictPin.Pointer != IntPtr.Zero) {
			FreeDict();
		}

		return result == 0;
	}

	private unsafe void FreeDict() {
		if ((nint) DictPin.Pointer != IntPtr.Zero) {
			DictPin.Dispose();
			DictPin = default;
			Dict = Memory<byte>.Empty;
		}
	}

	private void FreeContext() {
		if (DContext > 0) {
			DContext = NativeMethods.ZSTD_freeDCtx(DContext);
		}
	}

	private void ReleaseUnmanagedResources() {
		FreeContext();
		FreeDict();
	}

	~ZStandard() {
		ReleaseUnmanagedResources();
	}

	private static partial class NativeMethods {
	#if !NET8_0_OR_GREATER
		[DllImport("libzstd"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		internal static extern nint ZSTD_DCtx_setParameter(nint dctx, ZSTDParameter param, int value);

		[DllImport("libzstd"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		internal static extern unsafe nint ZSTD_DCtx_loadDictionary_advanced(nint dctx, byte* dict, nuint dictSize, ZSTDDictLoadMethod loadMethod, ZSTDDictContentType contentType);

		[DllImport("libzstd"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		internal static extern nint ZSTD_createDCtx();

		[DllImport("libzstd"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		internal static extern nint ZSTD_freeDCtx(nint dctx);

		[DllImport("libzstd"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		internal static extern unsafe nint ZSTD_decompressDCtx(nint dctx, byte* dst, int dstCapacity, byte* src, int srcSize);
	#else
		[LibraryImport("libzstd"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		internal static partial nint ZSTD_DCtx_setParameter(nint dctx, ZSTDParameter param, int value);

		[LibraryImport("libzstd"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		internal static unsafe partial nint ZSTD_DCtx_loadDictionary_advanced(nint dctx, byte* dict, nuint dictSize, ZSTDDictLoadMethod loadMethod, ZSTDDictContentType contentType);

		[LibraryImport("libzstd"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		internal static partial nint ZSTD_createDCtx();

		[LibraryImport("libzstd"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		internal static partial nint ZSTD_freeDCtx(nint dctx);

		[LibraryImport("libzstd"), DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
		internal static unsafe partial nint ZSTD_decompressDCtx(nint dctx, byte* dst, int dstCapacity, byte* src, int srcSize);
	#endif
	}
}
