using static JsonBinMin.JBMDecoder.DecodePoint;

namespace JsonBinMin;

internal partial class JBMDecoder
{
	internal static ReadOnlySpan<byte> DecodeMap => [
		IntInline, IntInline, IntInline, IntInline, IntInline, IntInline, IntInline, IntInline,
		IntInline, IntInline, IntInline, IntInline, IntInline, IntInline, IntInline, IntInline,
		IntInline, IntInline, IntInline, IntInline, IntInline, IntInline, IntInline, IntInline,
		IntInline, IntInline, IntInline, IntInline, IntInline, IntInline, IntInline, IntInline,
		DObject, DObject, DObject, DObject, DObject, DObject, DObject, DObject,
		DObject, DObject, DObject, DObject, DObject, DObject, DObject, DObject,
		DArray, DArray, DArray, DArray, DArray, DArray, DArray, DArray,
		DArray, DArray, DArray, DArray, DArray, DArray, DArray, DArray,
		DString, DString, DString, DString, DString, DString, DString, DString,
		DString, DString, DString, DString, DString, DString, DString, DString,

		Unused, Unused, Unused, Unused, Block101, Block101, Block101, Block101,
		Block101, Block101, Block101, Block101, Block101, Block101, Block101, Block101,

		Block110, Block110, Block110, Block110, Block110, Block110, Block110, Block110,
		Block110, Block110, Block110, Block110, Unused, Unused, Block110, Block110,

		NumStr, NumStr, NumStr, NumStr, NumStr, NumStr, NumStr, NumStr,
		False, True, Null, Unused, MetaDictDef, Unused, Unused, Unused,
	];

	internal static class DecodePoint
	{
		public const byte Unused = 0;
		public const byte IntInline = 1;
		public const byte DObject = 2;
		public const byte DArray = 3;
		public const byte DString = 4;
		public const byte Block101 = 5;
		public const byte Block110 = 6;
		public const byte NumStr = 7;
		public const byte False = 8;
		public const byte True = 9;
		public const byte Null = 10;
		public const byte MetaDictDef = 11;
	}
}
