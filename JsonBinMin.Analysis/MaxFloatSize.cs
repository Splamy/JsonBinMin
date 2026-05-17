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
				var strLen = halfVal.ToString(JbmEncoder.RoundtripHalfFormat, CultureInfo.InvariantCulture).Length;
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
				var strLen = half.ToString(JbmEncoder.RoundtripFloatFormat, CultureInfo.InvariantCulture).Length;
				return strLen;
			})
			.Max();
		return max + 1; // 1 for the sign bit
	}
}