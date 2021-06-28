namespace JsonBinMin
{
	internal enum JBMType : byte
	{
		#pragma warning disable
		//             [D TTT ????]
		Object      = 0b0_000_0000, // count < 15 [T, count] (X, Y)*count
		ObjectExt   = 0b0_000_1111, //            [T] [Int] (X, Y)*count
		Array       = 0b0_001_0000, // count < 15 [T, count] X*count
		ArrayExt    = 0b0_001_1111, //            [T] [Int] X*count
		String      = 0b0_010_0000, // len < 15 [T, len] ?X...?
		StringExt   = 0b0_010_1111, //          [T] [Int] ?X...?

		_Constants  = 0b0_011_0000,
		False       = 0b0_011_0000, // [T]
		True        = 0b0_011_0001, // [T]
		Null        = 0b0_011_0010, // [T]
		MetaDictDef = 0b0_011_1111, // [#=size] [X, Y, Z, ...]

		IntInline   = 0b0_100_0000, // [D TTT XXXX]

		//             [D TTT KKK N] T=Type K=Kind N=Negative
		_NumSized   = 0b0_101_000_0,
		Int8        = 0b0_101_000_0, // [T] #
		Int16       = 0b0_101_001_0, // [T] ##
		Int32       = 0b0_101_010_0, // [T] ####
		Int64       = 0b0_101_011_0, // [T] ########
		IntRle      = 0b0_101_100_0, // [T] ?X...?
		Float16     = 0b0_101_101_0, // [T] ## (Unused)
		Float32     = 0b0_101_110_0, // [T] ####
		Float64     = 0b0_101_111_0, // [T] ########

		//             [D TTT __ F N] T=Type F=Fraction N=Negative
		NumStr      = 0b0_110_00_0_0, // [T] ?NumStr...?
		// B64Str,HexStr ?
	}
}
