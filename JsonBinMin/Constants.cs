using System;

namespace JsonBinMin
{
	internal static class Constants
	{
		/// <summary><code>false</code></summary>
		public static ReadOnlySpan<byte> False => new[] { (byte)'f', (byte)'a', (byte)'l', (byte)'s', (byte)'e' };
		/// <summary><code>true</code></summary>
		public static ReadOnlySpan<byte> True => new[] { (byte)'t', (byte)'r', (byte)'u', (byte)'e' };
		/// <summary><code>null</code></summary>
		public static ReadOnlySpan<byte> Null => new[] { (byte)'n', (byte)'u', (byte)'l', (byte)'l' };

		/// <summary><code>0.</code></summary>
		public static ReadOnlySpan<byte> Fraction => new[] { (byte)'0', (byte)'.' };

		// Numbers (mostly) from https://source.dot.net/#System.Text.Json/System/Text/Json/JsonConstants.cs,141da09d6ed4b1f2
		public const int MaximumFormatUInt8Length = 3; // i.e. 255
		public const int MaximumFormatUInt16Length = 5; // i.e. 65535
		public const int MaximumFormatUInt24Length = 8; // i.e. 16777215
		public const int MaximumFormatUInt32Length = 10; // i.e. 4294967295
		public const int MaximumFormatUInt48Length = 15; // i.e. 281474976710655
		public const int MaximumFormatUInt64Length = 20;  // i.e. 18446744073709551615
		public const int MaximumFormatDoubleLength = 128;  // default (i.e. 'G'), using 128 (rather than say 32) to be future-proof.
		public const int MaximumFormatSingleLength = 128;  // default (i.e. 'G'), using 128 (rather than say 32) to be future-proof.
	}
}
