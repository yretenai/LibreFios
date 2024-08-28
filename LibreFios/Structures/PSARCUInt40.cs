using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#if NET8_0_OR_GREATER
using System.Numerics;
#endif

namespace LibreFios.Structures;

#if NET8_0_OR_GREATER
[StructLayout(LayoutKind.Sequential, Pack = 1), InlineArray(5)]
public struct PSARCUInt40 : IEquatable<PSARCUInt40>,
                            IComparisonOperators<PSARCUInt40, PSARCUInt40, bool>,
                            IComparisonOperators<PSARCUInt40, long, bool>,
                            IComparisonOperators<PSARCUInt40, int, bool> {
	private byte Value;
#else
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PSARCUInt40 : IEquatable<PSARCUInt40> {
	private unsafe fixed byte Value[0x5];

	public static unsafe implicit operator ReadOnlySpan<byte>(PSARCUInt40 hash) => new(hash.Value, 0x5);
	public static unsafe implicit operator Span<byte>(PSARCUInt40 hash) => new(hash.Value, 0x5);
	public unsafe byte this[int index] {
		get => index >= 0x5 ? throw new IndexOutOfRangeException() : Value[index];
		set {
			if (index > 0x5) {
				throw new IndexOutOfRangeException();
			}

			Value[index] = value;
		}
	}
#endif

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static implicit operator PSARCUInt40(long value) {
		var value40 = default(PSARCUInt40);
		value40[0] = (byte) ((value >> 32) & 0xFF);
		value40[1] = (byte) ((value >> 24) & 0xFF);
		value40[2] = (byte) ((value >> 16) & 0xFF);
		value40[3] = (byte) ((value >> 8) & 0xFF);
		value40[4] = (byte) (value & 0xFF);
		return value40;
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	public static implicit operator long(PSARCUInt40 value40) {
		var value = 0L;
		value |= (long) value40[0] << 32;
		value |= (long) value40[1] << 24;
		value |= (long) value40[2] << 16;
		value |= (long) value40[3] << 8;
		value |= value40[4];
		return value;
	}

	public override string ToString() => ((long) this).ToString();
	public bool Equals(PSARCUInt40 other) => this == other;
	public override bool Equals(object? obj) => obj is PSARCUInt40 other && Equals(other);
	public override int GetHashCode() => ((long) this).GetHashCode();
	public static bool operator ==(PSARCUInt40 left, PSARCUInt40 right) => (long) left == (long) right;
	public static bool operator !=(PSARCUInt40 left, PSARCUInt40 right) => (long) left != (long) right;
	public static bool operator >(PSARCUInt40 left, PSARCUInt40 right) => (long) left > (long) right;
	public static bool operator >=(PSARCUInt40 left, PSARCUInt40 right) => (long) left >= (long) right;
	public static bool operator <(PSARCUInt40 left, PSARCUInt40 right) => (long) left < (long) right;
	public static bool operator <=(PSARCUInt40 left, PSARCUInt40 right) => (long) left <= (long) right;
	public static bool operator ==(PSARCUInt40 left, long right) => (long) left == right;
	public static bool operator !=(PSARCUInt40 left, long right) => (long) left != right;
	public static bool operator >(PSARCUInt40 left, long right) => (long) left > right;
	public static bool operator >=(PSARCUInt40 left, long right) => (long) left >= right;
	public static bool operator <(PSARCUInt40 left, long right) => (long) left < right;
	public static bool operator <=(PSARCUInt40 left, long right) => (long) left <= right;
	public static bool operator ==(PSARCUInt40 left, int right) => (long) left == right;
	public static bool operator !=(PSARCUInt40 left, int right) => (long) left != right;
	public static bool operator >(PSARCUInt40 left, int right) => (long) left > right;
	public static bool operator >=(PSARCUInt40 left, int right) => (long) left >= right;
	public static bool operator <(PSARCUInt40 left, int right) => (long) left < right;
	public static bool operator <=(PSARCUInt40 left, int right) => (long) left <= right;
}
