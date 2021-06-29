using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using static JsonBinMin.Tests.AssertUtil;

namespace JsonBinMin.Tests
{
	[TestFixture]
	public class JsonBinMinTests
	{
		public static readonly string[] TestFiles = new[] {
			"simple_01.json",
			"simple_02.json",
			"simple_03.json",
			"simple_04.json",

			"opts_01.json",

			"nums_01.json",
			"nums_02.json",
			"nums_03.json",

			"big_01.json",
		};

		[Test, TestCaseSource(nameof(TestFiles))]
		public void RoundTrip(string file)
		{
			var json = File.ReadAllText(Path.Combine("Assets", file));

			var compressed = JBMConverter.Compress(json, new() { UseDict = true });
			Console.WriteLine("LENGTH: {0}JS -> {1}JBM", json.Length, compressed.Length);
			Directory.CreateDirectory("Compressed");
			File.WriteAllBytes(Path.Combine("Compressed", file + ".bin"), compressed);
			var roundtrip = JBMConverter.DecompressToString(compressed);

			AssertStructuralEqual(json, roundtrip, JBMOptions.Default);
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
						CompressCtx.WriteNumberValue(formatted, mem, JBMOptions.Default);

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
}
