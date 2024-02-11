using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using JsonBinMin.BinV1;
using NUnit.Framework;
using static JsonBinMin.Tests.AssertUtil;

namespace JsonBinMin.Tests;

[TestFixture]
public class JsonBinMinTests
{
	public static IEnumerable<object> TestFiles()
	{
		foreach (var useDict in Enum.GetValues<UseDict>())
		{
			yield return new object[] { "simple_01.json", 576, 308, 299, useDict };
			yield return new object[] { "simple_02.json", 376, 140, 115, useDict };
			yield return new object[] { "simple_03.json", 601, 312, 280, useDict };
			yield return new object[] { "simple_04.json", 3821, 2473, 2282, useDict };
			yield return new object[] { "opts_01.json", 416, 230, 230, useDict };
			yield return new object[] { "nums_01.json", 750, 420, 420, useDict };
			yield return new object[] { "nums_02.json", 11229, 6155, 6155, useDict };
			yield return new object[] { "nums_03.json", 18805, 10946, 10811, useDict };
			yield return new object[] { "big_01.json", 5796673, 2500839, 782977, useDict };
			yield return new object[] { "big_02.json", 45467800, 18641756, 7828683, useDict };

			yield return new object[] { "test.unicode.json", 15487, 12860, 8327, useDict };
			yield return new object[] { "unicode.json", 3568, 1592, 1208, useDict };
		}
	}

	[Test, TestCaseSource(nameof(TestFiles))]
	public void TestInvariants(string _file, int originalSize, int compressedDictOff, int compressedDictSimple, UseDict _useDict)
	{
		Assert.LessOrEqual(compressedDictOff, originalSize); // Compressed should be smaller than original
		Assert.LessOrEqual(compressedDictSimple, compressedDictOff); // Simple dict should be strictly smaller than no dict
	}

	[Test, TestCaseSource(nameof(TestFiles))]
	public void RoundTrip(string file, int originalSize,
		int compressedDictOff, int compressedDictSimple, UseDict useDict)
	{
		var compressedSize = useDict switch
		{
			UseDict.Off => compressedDictOff,
			UseDict.Simple => compressedDictSimple,
			_ => throw new ArgumentOutOfRangeException(nameof(useDict), useDict, null),
		};

		var json = File.ReadAllBytes(Path.Combine("Assets", file));
		var options = new JBMOptions()
		{
			UseDict = useDict,
			UseFloats = UseFloats.Double | UseFloats.Single | UseFloats.Half,
			UseJbm = true,
			UseAos = false,
			Compress = false,
		};
		var compressed = JBMConverter.Encode(json, options);
		Console.WriteLine("LENGTH: {0}JS -> {1}JBM", json.Length, compressed.Length);
		Assert.AreEqual(json.Length, originalSize);
		Assert.LessOrEqual(compressed.Length, compressedSize);
		if (compressed.Length < compressedSize)
		{
			Assert.Warn("Compressed size is smaller than expected. Please update test");
		}
		Directory.CreateDirectory("Compressed");
		File.WriteAllBytes(Path.Combine("Compressed", file + ".bin"), compressed);
		var roundtrip = JBMConverter.DecodeToString(compressed);

		var jsonString = Encoding.UTF8.GetString(json);
		AssertStructuralEqual(jsonString, roundtrip);
	}

	[Test]
	public void TestLengthsOfNumbers()
	{
		for (var m = 0; m <= 1; m++)
		{
			for (int i = 0; i < 128; i++)
			{
				for (int v = -1; v <= 1; v++)
				{
					var val = (System.Numerics.BigInteger.One << i) + v;
					var neg = m == 1;
					var formatted = $"{(neg ? "-" : "")}{val}";

					var mem = new MemoryStream();
					JBMEncoder.WriteNumberValue(formatted, mem, JBMOptions.Default);

					// 1 byte type
					long maxSize = 1;
					// n bytes for bits
					maxSize += i switch
					{
						< 8 => 1,
						< 16 => 2,
						< 24 => 3,
						< 32 => 4,
						< 48 => 6,
						< 64 => 8,
						>= 64 => (i / 7) + 1,
					};

					if (i % 8 == 0 && !neg && v == 1)
						maxSize += 1;
					else if (i % 8 == 0 && neg && v == -1)
						maxSize += 1;
					Assert.LessOrEqual(mem.Length, maxSize, "Number {0} should be stored in {1} bytes", formatted, maxSize);
				}
			}
		}
	}

	[Test]
	public void TestFlagCombinations([Values] UseDict useDict, [Values] bool useAos, [Values] bool useCompression, [Values] bool useJbm)
	{
		var file = "simple_01.json";
		var json = File.ReadAllText(Path.Combine("Assets", file));

		var options = new JBMOptions() { UseDict = useDict, Compress = useCompression, UseAos = useAos, UseJbm = useJbm };
		var compressed = JBMConverter.Encode(json, options);
		var roundtrip = JBMConverter.DecodeToString(compressed);

		AssertStructuralEqual(json, roundtrip);
	}

	[Test]
	public void TestDictEntriesInExtValues()
	{
		var strb = new StringBuilder();
		const int num = 1000;
		strb.Append('[');
		for (int i = 0; i < num; i++)
		{
			strb.Append(num);
			if (i != num - 1)
				strb.Append(',');
		}
		strb.Append(']');

		var json = strb.ToString();

		var options = new JBMOptions() { UseDict = UseDict.Simple };
		var compressed = JBMConverter.Encode(json, options);
		var roundtrip = JBMConverter.DecodeToString(compressed, options);
		AssertStructuralEqual(json, roundtrip);
	}

	[Test]
	public void HalfIsBetterForAtLeastOneNumber()
	{
		var optWithHalf = new JBMOptions() { UseFloats = UseFloats.Half };

		static bool IsHalfEncoded(byte[] data) => data.Length == 3 && data[0] == (byte)JBMType.Float16;
		var mem = new MemoryStream();

		var found = new List<Half>();

		for (ushort i = 0; i < ushort.MaxValue; i++)
		{
			var half = Unsafe.As<ushort, Half>(ref i);
			if (Half.IsNaN(half) || Half.IsInfinity(half))
				continue;
			var halfStr = half.ToString(CultureInfo.InvariantCulture);

			mem.SetLength(0);
			JBMEncoder.WriteNumberValue(halfStr, mem, optWithHalf);
			var enc = mem.ToArray();

			if (IsHalfEncoded(enc))
			{
				found.Add(half);
			}
		}

		if (found.Count == 0)
			Assert.Fail();
	}
}

// Codecov, following here:
// - https://docs.microsoft.com/en-gb/dotnet/core/testing/unit-testing-code-coverage?tabs=windows
// dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura
// reportgenerator -reports:coverage.cobertura.xml -targetdir:coverage -reporttypes:Html
