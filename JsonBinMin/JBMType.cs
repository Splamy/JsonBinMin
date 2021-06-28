namespace JsonBinMin
{
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
		False       = 0b0_101_0000, // [T]
		True        = 0b0_101_0001, // [T]
		Null        = 0b0_101_0010, // [T]
		Float16     = 0b0_101_0011, // [T] ## (Not Implemented)
		Float32     = 0b0_101_0100, // [T] ####
		Float64     = 0b0_101_0101, // [T] ########
		MetaDictDef = 0b0_101_1111, // [#=size] [X, Y, Z, ...]

		//             [D TTT KKK N] T=Type K=Kind N=Negative
		_Block110   = 0b0_110_000_0,
		Int8        = 0b0_110_000_0, // [T] #
		Int16       = 0b0_110_001_0, // [T] ##
		Int24       = 0b0_110_010_0, // [T] ### (Not Implemented)
		Int32       = 0b0_110_011_0, // [T] ####
		Int48       = 0b0_110_100_0, // [T] ###### (Not Implemented)
		Int64       = 0b0_110_101_0, // [T] ########
		IntRle      = 0b0_110_111_0, // [T] ?X...?

		//             [D TTT __ F N] T=Type F=Fraction N=Negative
		NumStr      = 0b0_111_00_0_0, // [T] ?NumStr...?
		// B64Str,HexStr ?
	}
}
