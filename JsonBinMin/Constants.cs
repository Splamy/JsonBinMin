using System;
using System.Numerics;

namespace JsonBinMin;

internal static class Constants
{
	/// <summary><code>false</code></summary>
	public static ReadOnlySpan<byte> False => "false"u8;
	/// <summary><code>true</code></summary>
	public static ReadOnlySpan<byte> True => "true"u8;
	/// <summary><code>null</code></summary>
	public static ReadOnlySpan<byte> Null => "null"u8;

	/// <summary><code>0.</code></summary>
	public static ReadOnlySpan<byte> Leading0 => "0."u8;
	/// <summary><code>.0</code></summary>
	public static ReadOnlySpan<byte> Tailing0 => ".0"u8;
	/// <summary><code>.0</code></summary>
	public static ReadOnlySpan<byte> Float0 => "0.0"u8;

	// Numbers (mostly) from https://source.dot.net/#System.Text.Json/System/Text/Json/JsonConstants.cs,141da09d6ed4b1f2
	public const int MaximumFormatUInt8Length = 3; // i.e. 255
	public const int MaximumFormatUInt16Length = 5; // i.e. 65535
	public const int MaximumFormatUInt24Length = 8; // i.e. 16777215
	public const int MaximumFormatUInt32Length = 10; // i.e. 4294967295
	public const int MaximumFormatUInt48Length = 15; // i.e. 281474976710655
	public const int MaximumFormatUInt64Length = 20;  // i.e. 18446744073709551615
	public const int MaximumFormatDoubleLength = 128;  // default (i.e. 'G'), using 128 (rather than say 32) to be future-proof.
	public const int MaximumFormatSingleLength = 128;  // default (i.e. 'G'), using 128 (rather than say 32) to be future-proof.

	public const uint U24MaxValue = (1U << 24) - 1;
	public const ulong U48MaxValue = (1UL << 48) - 1;

	public const uint JbmIntInlineMaxValue = 31;
	public const uint JbmInt8MaxValue = byte.MaxValue;
	public const uint JbmInt16MaxValue = JbmInt16Offset + ushort.MaxValue;
	public const uint JbmInt24MaxValue = JbmInt24Offset + U24MaxValue;
	public const ulong JbmInt32MaxValue = (ulong)JbmInt32Offset + (ulong)uint.MaxValue;
	public const ulong JbmInt48MaxValue = JbmInt48Offset + U48MaxValue;
	public static readonly BigInteger JbmInt64MaxValue = new BigInteger(JbmInt64Offset) + new BigInteger(ulong.MaxValue);

	public const uint JbmInt8Offset = 0;
	public const uint JbmInt16Offset = JbmInt8MaxValue + 1;
	public const uint JbmInt24Offset = JbmInt16MaxValue + 1;
	public const uint JbmInt32Offset = JbmInt24MaxValue + 1;
	public const ulong JbmInt48Offset = JbmInt32MaxValue + 1;
	public const ulong JbmInt64Offset = JbmInt48MaxValue + 1;
	public static readonly BigInteger JbmIntRleOffset = JbmInt64MaxValue + 1;
}
