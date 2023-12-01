namespace JsonBinMin;

internal enum JBMType : byte
{
#pragma warning disable
	//             [D TT XXXXX]
	IntInline   = 0b0_00_00000,
	//             [D TTT ????]
	Object      = 0b0_010_0000, // count < 15 [T, count] (X, Y)*count
	ObjectExt   = 0b0_010_1111, //            [T] [Int] (X, Y)*count
	Array       = 0b0_011_0000, // count < 15 [T, count] X*count
	ArrayExt    = 0b0_011_1111, //            [T] [Int] X*count
	String      = 0b0_100_0000, //   len < 15 [T, len] ?X...?
	StringExt   = 0b0_100_1111, //            [T] [Int] ?X...?

	_Block101   = 0b0_101_0000,
	//             [D TTT KK T U] T=Type K=Kind T=Tailing'.0' U=UpperCase-'E' for exponent
	Float16     = 0b0_101_01_0_0, // [T] ##
	Float32     = 0b0_101_10_0_0, // [T] ####
	Float64     = 0b0_101_11_0_0, // [T] ########

	//             [D TTT KKK N] T=Type K=Kind N=Negative
	_Block110   = 0b0_110_000_0,
	Int8        = 0b0_110_000_0, // [T] #
	Int16       = 0b0_110_001_0, // [T] ##
	Int24       = 0b0_110_010_0, // [T] ### = [SS][B] LSB -> MSB
	Int32       = 0b0_110_011_0, // [T] ####
	Int48       = 0b0_110_100_0, // [T] ###### = [IIII][SS] LSB -> MSB
	Int64       = 0b0_110_101_0, // [T] ########
	IntRle      = 0b0_110_111_0, // [T] [rle-count] X*count

	_Block111   = 0b0_111_000_0,
	//             [D TTTT L T N] T=Type _=Unused L=Leading'0.' T=Tailing'.0' N=Negative
	NumStr      = 0b0_1110_0_0_0, // [T] ?NumStr...?

	//             [D TTTT KKK] T=Type K=Kind
	False       = 0b0_1111_000, // [T]
	True        = 0b0_1111_001, // [T]
	Null        = 0b0_1111_010, // [T]
	MetaDictDef = 0b0_1111_100, // [T] [#=size] [X, Y, Z, ...]
	Compressed  = 0b0_1111_101, // [T] rest... (Only allowed as the first byte overall)

	// B64Str,HexStr ?
}
