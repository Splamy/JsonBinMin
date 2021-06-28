```
enum JBMType : byte
{
	Object           =   1, // [T] [IntRle] (X, Y)*count
	Array            =   2, // [T] [IntRle] X*count

	String           =   8, // [T] [IntRle] ?X...?

	True             =  16, // [T]
	False            =  17, // [T]
	Null             =  18, // [T]

	Int8             =  32, // [T] #
	Int16            =  33, // [T] ##
	Int32            =  34, // [T] ####
	IntI64           =  35, // [T] ########
	IntU64           =  36, // [T] ########
	IntRle           =  37, // [T] ?X...?

	Float32          =  48, // [T] ####
	Float64          =  49, // [T] ########
	FloatTupleRle    =  50, // [T] [IntRle] [IntRle] as (X.Y)
	FloatStr         =  51, // [T] [String]

	MetaDictDef      =  64, // [X, Y, Z, ...]
	MetaDictUpdate   =  66, // ! Not implemented

	ConstInt0        =  80, // [T] = 0
	ConstInt1        =  81, // [T] = 1
	ConstEmptyStr    =  82, // [T] = ""
	ConstEmptyArr    =  83, // [T] = []
	ConstEmptyObj    =  84, // [T] = {}
}

[T] & 0x80 != 0:
	var dictRef = [T] & 0x7F;
```