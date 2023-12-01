using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
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
			yield return new object[] { "simple_01.json", 576, 307, 298, 298, useDict };
			yield return new object[] { "simple_02.json", 376, 139, 114, 114, useDict };
			yield return new object[] { "simple_03.json", 601, 311, 279, 279, useDict };
			yield return new object[] { "simple_04.json", 3821, 2472, 2281, 2281, useDict };
			yield return new object[] { "opts_01.json", 416, 229, 229, 229, useDict };
			yield return new object[] { "nums_01.json", 750, 419, 419, 419, useDict };
			yield return new object[] { "nums_02.json", 11229, 6154, 6154, 6154, useDict };
			yield return new object[] { "nums_03.json", 18805, 10945, 10810, 10810, useDict };
			yield return new object[] { "big_01.json", 5796673, 2500838, 782976, 782976, useDict };
			yield return new object[] { "big_02.json", 45467800, 18641755, 7828682, 7828682, useDict };
		}
	}

	[Test, TestCaseSource(nameof(TestFiles))]
	public void TestInvariants(string _file, int _originalSize, int compressedDictOff, int compressedDictSimple, int compressedDictDeep, UseDict _useDict)
	{
		Assert.LessOrEqual(compressedDictSimple, compressedDictOff); // Simple dict should be strictly smaller than no dict
		Assert.LessOrEqual(compressedDictDeep, compressedDictSimple); // Deep dict should be strictly smaller than simple dict
	}

	[Test, TestCaseSource(nameof(TestFiles))]
	public void RoundTrip(string file, int originalSize,
		int compressedDictOff, int compressedDictSimple, int compressedDictDeep, UseDict useDict)
	{
		var compressedSize = useDict switch
		{
			UseDict.Off => compressedDictOff,
			UseDict.Simple => compressedDictSimple,
			UseDict.Deep => compressedDictDeep,
			_ => throw new ArgumentOutOfRangeException(nameof(useDict), useDict, null),
		};

		var json = File.ReadAllText(Path.Combine("Assets", file));
		var options = new JBMOptions()
		{
			UseDict = useDict,
			UseFloats = UseFloats.Double | UseFloats.Single | UseFloats.Half,
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

		AssertStructuralEqual(json, roundtrip, options);
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
	public void TestCompression([Values] UseDict useDict)
	{
		var file = "simple_01.json";
		var json = File.ReadAllText(Path.Combine("Assets", file));

		var options = new JBMOptions() { UseDict = useDict, Compress = true };
		var compressed = JBMConverter.Encode(json, options);
		var roundtrip = JBMConverter.DecodeToString(compressed);

		AssertStructuralEqual(json, roundtrip, options);
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

public static class AssertUtil
{
	public static void AssertStructuralEqual(string jsonExprected, string jsonActual, JBMOptions options)
	{
		var expected = JsonSerializer.Deserialize<JsonElement>(jsonExprected);
		var actual = JsonSerializer.Deserialize<JsonElement>(jsonActual);
		AssertStructuralEqual(expected, actual, options);
	}

	public static void AssertStructuralEqual(JsonElement jsonExpected, JsonElement jsonActual, JBMOptions options)
	{
		Assert.AreEqual(jsonExpected.ValueKind, jsonActual.ValueKind);
		switch (jsonExpected.ValueKind)
		{
		case JsonValueKind.Object:
			foreach (var (expected, actual) in jsonExpected.EnumerateObject().OrderBy(j => j.Name).Zip(jsonActual.EnumerateObject().OrderBy(j => j.Name)))
			{
				Assert.AreEqual(expected.Name, actual.Name);
				AssertStructuralEqual(expected.Value, actual.Value, options);
			}
			break;
		case JsonValueKind.Array:
			foreach (var (expected, actual) in jsonExpected.EnumerateArray().Zip(jsonActual.EnumerateArray()))
				AssertStructuralEqual(expected, actual, options);
			break;
		case JsonValueKind.String:
			Assert.AreEqual(jsonExpected.GetString(), jsonActual.GetString());
			break;
		case JsonValueKind.Number:
			Assert.AreEqual(jsonExpected.GetRawText(), jsonActual.GetRawText());
			break;
		case JsonValueKind.Undefined:
		case JsonValueKind.True:
		case JsonValueKind.False:
		case JsonValueKind.Null:
			return;
		case var unhandled:
			throw new MissingMemberException("Missing case:" + unhandled.ToString());
		}
	}
}

// Codecov, following here:
// - https://docs.microsoft.com/en-gb/dotnet/core/testing/unit-testing-code-coverage?tabs=windows
// dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura
// reportgenerator -reports:coverage.cobertura.xml -targetdir:coverage -reporttypes:Html
