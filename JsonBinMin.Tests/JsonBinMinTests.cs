
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

			"floats_01.json",

			"big_01.json",
		};

		[Test, TestCaseSource(nameof(TestFiles))]
		public void RoundTrip(string file)
		{
			var json = File.ReadAllText(Path.Combine("Assets", file));

			var compressed = JsonBinMin.Compress(json);
			Console.WriteLine("LENGTH: {0}JS -> {1}JBM", json.Length, compressed.Length);
			Directory.CreateDirectory("Compressed");
			File.WriteAllBytes(Path.Combine("Compressed", file + ".bin"), compressed);
			var roundtrip = JsonBinMin.Decompress(compressed);

			AssertStructuralEqual(json, roundtrip, JsonBinMinOptions.Default);
		}
	}

	public static class AssertUtil
	{
		public static void AssertStructuralEqual(string jsonExprected, string jsonActual, JsonBinMinOptions options)
		{
			var expected = JsonSerializer.Deserialize<JsonElement>(jsonExprected);
			var actual = JsonSerializer.Deserialize<JsonElement>(jsonActual);
			AssertStructuralEqual(expected, actual, options);
		}

		public static void AssertStructuralEqual(JsonElement jsonExprected, JsonElement jsonActual, JsonBinMinOptions options)
		{
			Assert.AreEqual(jsonExprected.ValueKind, jsonActual.ValueKind);
			switch (jsonExprected.ValueKind)
			{
				case JsonValueKind.Object:
					foreach (var (expected, actual) in jsonExprected.EnumerateObject().OrderBy(j => j.Name).Zip(jsonActual.EnumerateObject().OrderBy(j => j.Name)))
					{
						Assert.AreEqual(expected.Name, actual.Name);
						AssertStructuralEqual(expected.Value, actual.Value, options);
					}
					break;
				case JsonValueKind.Array:
					foreach (var (expected, actual) in jsonExprected.EnumerateArray().Zip(jsonActual.EnumerateArray()))
						AssertStructuralEqual(expected, actual, options);
					break;
				case JsonValueKind.String:
					Assert.AreEqual(jsonExprected.GetString(), jsonActual.GetString());
					break;
				case JsonValueKind.Number:
					Assert.AreEqual(jsonExprected.GetRawText(), jsonExprected.GetRawText());
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
}
