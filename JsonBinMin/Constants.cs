using System;

namespace JsonBinMin
{
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
	}
}
