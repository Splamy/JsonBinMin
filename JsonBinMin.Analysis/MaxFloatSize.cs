using System.Globalization;
using System.Runtime.CompilerServices;
using JsonBinMin.BinV1;

namespace JsonBinMin.Analysis;

public static class MaxFloatSize
{
	public static int GetMaxHalfSize()
	{
		var max = Enumerable.Range(0, short.MaxValue)
			.AsParallel()
			.Select(raw => {
				var rawUshort = (ushort)raw;
				var halfVal = Unsafe.As<ushort, Half>(ref rawUshort);
				var strLen = halfVal.ToString(Constants.RoundtripHalfFormat, CultureInfo.InvariantCulture).Length;
				return strLen;
			})
			.Max();
		return max + 1; // 1 for the sign bit
	}
	
	public static int GetMaxFloatSize()
	{
		var max = Enumerable.Range(0, int.MaxValue)
			.AsParallel()
			.Select(raw => {
				var rawUInt = (uint)raw;
				var half = Unsafe.As<uint, float>(ref rawUInt);
				var strLen = half.ToString(Constants.RoundtripFloatFormat, CultureInfo.InvariantCulture).Length;
				return strLen;
			})
			.Max();
		return max + 1; // 1 for the sign bit
	}
	
	public static int GetMaxDoubleSize()
	{
		var max = GetInterestingBits()
			.AsParallel()
			.Select(raw => {
				var rawUInt = raw;
				var half = Unsafe.As<ulong, double>(ref rawUInt);
				var strLen = half.ToString(Constants.RoundtripDoubleFormat, CultureInfo.InvariantCulture).Length;
				return strLen;
			})
			.Max();
		return max + 1; // 1 for the sign bit
	}

	private static IEnumerable<ulong> GetInterestingBits()
	{
		yield return 0; // zero
		yield return 1; // smallest denormalized number
		yield return 0x0000000000000001; // smallest denormalized number
		yield return 0x0000000000000002; // second smallest denormalized number
		yield return 0x0000000000000003; // third smallest denormalized number
		yield return 0x7FF0000000000000; // infinity
		yield return 0x7FF8000000000000; // NaN
	}
}