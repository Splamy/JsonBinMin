using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using JsonBinMin.BinV1;

namespace JsonBinMin.Tests;

[TestClass]
public class JsonBinMinTests
{
	public static IEnumerable<object?[]> TestFiles()
	{
		foreach (var useDict in Enum.GetValues<UseDict>())
		{
			yield return ["big_01.json", 3523431, 2500839, 782977, useDict];
			//yield return new object[] { "big_02.json", 45467800, 18641756, 7828683, useDict };
			yield return ["nums_01.json", 655, 420, 420, useDict];
			yield return ["nums_02.json", 9272, 6155, 6155, useDict];
			yield return ["nums_03.json", 16212, 10946, 10811, useDict];
			yield return ["opts_01.json", 313, 230, 230, useDict];
			yield return ["simple_01.json", 360, 308, 299, useDict];
			yield return ["simple_02.json", 183, 140, 115, useDict];
			yield return ["simple_03.json", 389, 312, 280, useDict];
			yield return ["simple_04.json", 2710, 2473, 2282, useDict];


			yield return ["test.unicode.json", 15486, 12860, 8327, useDict];
			yield return ["unicode.json", 2186, 1592, 1208, useDict];
		}
	}

	[TestMethod, DynamicData(nameof(TestFiles))]
	public void TestInvariants(string _file, int originalSize, int compressedDictOff, int compressedDictSimple,
		UseDict _useDict)
	{
		compressedDictOff.ShouldBeLessThanOrEqualTo(originalSize, "Compressed file should be smaller than original");
		compressedDictSimple.ShouldBeLessThanOrEqualTo(compressedDictOff,
			"Simple dict should be strictly smaller than no dict");
	}

	[TestMethod, DynamicData(nameof(TestFiles))]
	public void RoundTrip(string file, int originalSize,
		int compressedDictOff, int compressedDictSimple, UseDict useDict)
	{
		var compressedSize = useDict switch
		{
			UseDict.Off => compressedDictOff,
			UseDict.Simple => compressedDictSimple,
			_ => throw new ArgumentOutOfRangeException(nameof(useDict), useDict, null),
		};

		var json = GetNormalizedJson(file);
		Assert.AreEqual(originalSize, json.Length);

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
		compressed.Length.ShouldBeLessThanOrEqualTo(compressedSize);

		var compressedExpect = GetExpectBytes(file, useDict);
		CollectionAssert.AreEqual(compressedExpect, compressed, "Compressed output should match expected");

		Directory.CreateDirectory("Compressed");
		File.WriteAllBytes(Path.Combine("Compressed", GetExpectFileName(file, useDict)), compressed);
		var roundtrip = JBMConverter.DecodeToString(compressed);

		var jsonText = Encoding.UTF8.GetString(json);
		AssertStructuralEqual(jsonText, roundtrip);
		
		if (compressed.Length < compressedSize)
		{
			Assert.Inconclusive("Compressed size is smaller than expected. Please update test data." +
			                    $"Expected size: {compressedSize}, actual size: {compressed.Length}");
		}
	}
	
	[TestMethod, DynamicData(nameof(TestFiles))]
	public void BackwardsEqual(string file, int originalSize,
		int compressedDictOff, int compressedDictSimple, UseDict useDict)
	{
		var compressed = GetExpectBytes(file, useDict);
		var roundtrip = JBMConverter.DecodeToString(compressed);

		var expectedJson = GetNormalizedJson(file);
		var jsonText = Encoding.UTF8.GetString(expectedJson);
		AssertStructuralEqual(jsonText, roundtrip);
	}

	private static JsonSerializerOptions Options => new()
	{
		AllowTrailingCommas = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		WriteIndented = false,
	};

	private static byte[] GetNormalizedJson(string file)
	{
		using var fs = File.OpenRead(Path.Combine("Assets", file));
		var json = JsonSerializer.Deserialize<JsonElement>(fs, Options);
		return JsonSerializer.SerializeToUtf8Bytes(json, Options);
	}

	private static string GetExpectFileName(string file, UseDict useDict)
		=> $"{file}.{useDict}.bin";

	private static byte[] GetExpectBytes(string file, UseDict useDict)
		=> File.ReadAllBytes(Path.Combine("Expects", GetExpectFileName(file, useDict)));

	[TestMethod]
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
					mem.Length.ShouldBeLessThanOrEqualTo(maxSize,
						$"Number {formatted} should be stored in {maxSize} bytes");
				}
			}
		}
	}

	public static IEnumerable<object[]> TestFlagCombinationsValues =>
		from useDict in Enum.GetValues<UseDict>()
		from useAos in new[] { false, true }
		from useCompression in new[] { false, true }
		from useJbm in new[] { false, true }
		select new object[] { useDict, useAos, useCompression, useJbm };

	[TestMethod]
	[DynamicData(nameof(TestFlagCombinationsValues))]
	public void TestFlagCombinations(UseDict useDict, bool useAos, bool useCompression, bool useJbm)
	{
		var file = "simple_01.json";
		var json = File.ReadAllText(Path.Combine("Assets", file));

		var options =
			new JBMOptions() { UseDict = useDict, Compress = useCompression, UseAos = useAos, UseJbm = useJbm };
		var compressed = JBMConverter.Encode(json, options);
		var roundtrip = JBMConverter.DecodeToString(compressed);

		AssertStructuralEqual(json, roundtrip);
	}

	[TestMethod]
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

	[TestMethod]
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
		{
			Assert.Fail();
		}
	}
}

// Codecov, following here:
// - https://docs.microsoft.com/en-gb/dotnet/core/testing/unit-testing-code-coverage?tabs=windows
// dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura
// reportgenerator -reports:coverage.cobertura.xml -targetdir:coverage -reporttypes:Html