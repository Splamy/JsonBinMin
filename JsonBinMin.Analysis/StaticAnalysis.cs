using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace JsonBinMin.Analysis;

internal class StaticAnalysis
{
	private static readonly JBMType[] TypeMap;

	private static readonly JBMOptions jbmOptions = new()
	{
		Compress = false,
		UseDict = UseDict.Off,
		UseFloats = UseFloats.All,
	};

	static StaticAnalysis()
	{
		TypeMap = new JBMType[256];
		for (int i = 0; i < 256; i++)
		{
			TypeMap[i] = GetNumberType([(byte)i]);
		}
	}

	public static void Run()
	{
		var results = ParalellRange(0, uint.MaxValue);
		var strb = new StringBuilder();

		foreach (var result in results)
		{
			strb.AppendFormat(CultureInfo.InvariantCulture, "[{0} - {1}]: {2}\n", As(result.Start), As(result.End), AnalysisReport.FType(result.Type));
		}

		var summary = strb.ToString();
		File.WriteAllText("summary.txt", summary);
		Console.WriteLine(summary);
	}

	public static List<SliceResult> ParalellRange(long from, long to)
	{
		List<SliceResult> results = [];
		var splits = SplitRangeIntoEqualParts(from, to);

		Parallel.ForEach(
			splits,
			new ParallelOptions()
			{
				MaxDegreeOfParallelism = splits.Length
			},
			range => {
				var result = AnalyzeRange(range.Item1, range.Item2);
				lock (results)
				{
					results.AddRange(result);
				}
			}
		);

		results.Sort((a, b) => As(a.Start).CompareTo(As(b.Start)));

		// Merge buckets
		for (int i = 0; i < results.Count - 1; i++)
		{
			var a = results[i];
			var b = results[i + 1];
			if (a.Type == b.Type)
			{
				results[i] = new SliceResult(a.Start, b.End, a.Type);
				results.RemoveAt(i + 1);
				i--;
			}
		}

		return results;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static float As(long num)
	{
		var c = unchecked((uint)num);
		return Unsafe.As<uint, float>(ref c);
	}

	public static (long, long)[] SplitRangeIntoEqualParts(long from, long to)
	{
		var splitCount = Environment.ProcessorCount;
		var result = new (long, long)[splitCount];
		var step = (to - from) / splitCount;
		for (int i = 0; i < result.Length; i++)
		{
			result[i] = (from + step * i, from + step * (i + 1));
		}

		return result;
	}

	public static List<SliceResult> AnalyzeRange(long from, long to)
	{
		var result = new List<SliceResult>();
		var mem = new MemoryStream(new byte[128], 0, 128, true, true);

		JBMEncoder.WriteNumberValue(from.ToString(CultureInfo.InvariantCulture), mem, jbmOptions);
		JBMType bucketType = TypeMap[mem.GetBuffer()[0]];

		long bucketStart = from;

		for (long i = from; i < to; i++)
		{
			mem.SetLength(0);
			var f = As(i);
			if (!float.IsRealNumber(f) || !float.IsFinite(f))
			{
				continue;
			}
			JBMEncoder.WriteNumberValue(f.ToString(CultureInfo.InvariantCulture), mem, jbmOptions);
			var type = TypeMap[mem.GetBuffer()[0]];

			if (bucketType != type)
			{
				result.Add(new SliceResult(bucketStart, i - 1, bucketType));
				bucketStart = i;
				bucketType = type;
			}
		}

		result.Add(new SliceResult(bucketStart, to, bucketType));
		return result;
	}

	public static JBMType GetNumberType(ReadOnlySpan<byte> data)
	{
		var pick = data[0];

		if ((JBMType)(pick & 0b1_11_00000) == 0) // IntInline
		{
			return JBMType.IntInline;
		}

		switch ((JBMType)(pick & 0b1_111_0000))
		{
		case JBMType.NumStr:
			return JBMType.NumStr;
		}

		if ((pick & 0b1_111_00_0_0) == 0b0_101_00_0_0 && (pick & 0b0_000_11_0_0) != 0)
		{
			return (JBMType)(pick & 0b1_111_11_0_0);
		}

		return (JBMType)(pick & 0b1_111_111_0) switch
		{
			JBMType.Int8 => JBMType.Int8,
			JBMType.Int16 => JBMType.Int16,
			JBMType.Int24 => JBMType.Int24,
			JBMType.Int32 => JBMType.Int32,
			JBMType.Int48 => JBMType.Int48,
			JBMType.Int64 => JBMType.Int64,
			JBMType.IntRle => JBMType.IntRle,
			_ => (JBMType)255,
		};
	}
}

record SliceResult(long Start, long End, JBMType Type);