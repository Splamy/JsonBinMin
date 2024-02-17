using System;
using System.Linq;
using System.Text.Json;
using Json.Pointer;

namespace JsonBinMin.Tests;

public static class AssertUtil
{
	public static void AssertStructuralEqual(string jsonExprected, string jsonActual)
	{
		var expected = JsonSerializer.Deserialize<JsonElement>(jsonExprected);
		var actual = JsonSerializer.Deserialize<JsonElement>(jsonActual);
		AssertStructuralEqual(expected, actual, JsonPointer.Empty);
	}

	public static void AssertStructuralEqual(JsonElement jsonExpected, JsonElement jsonActual, JsonPointer dbgPath)
	{
		const string FailedOnPath = "Failed on path: {0}";

		Assert.AreEqual(jsonExpected.ValueKind, jsonActual.ValueKind, "Type mismatch on {0}", dbgPath);
		switch (jsonExpected.ValueKind)
		{
		case JsonValueKind.Object:
			var o1 = jsonExpected.EnumerateObject().OrderBy(j => j.Name).ToList();
			var o2 = jsonActual.EnumerateObject().OrderBy(j => j.Name).ToList();
			if (o1.Count != o2.Count)
				Assert.Fail("Object length mismatch on {0}", dbgPath);
			foreach (var (expected, actual) in o1.Zip(o2))
			{
				Assert.AreEqual(expected.Name, actual.Name, FailedOnPath, dbgPath);
				AssertStructuralEqual(expected.Value, actual.Value, dbgPath.Combine(expected.Name));
			}
			break;
		case JsonValueKind.Array:
			if (jsonExpected.GetArrayLength() != jsonActual.GetArrayLength())
				Assert.Fail("Array length mismatch on {0}", dbgPath);
			int i = 0;
			foreach (var (expected, actual) in jsonExpected.EnumerateArray().Zip(jsonActual.EnumerateArray()))
				AssertStructuralEqual(expected, actual, dbgPath.Combine(i++));
			break;
		case JsonValueKind.String:
			Assert.AreEqual(jsonExpected.GetString(), jsonActual.GetString(), FailedOnPath, dbgPath);
			break;
		case JsonValueKind.Number:
			Assert.AreEqual(jsonExpected.GetRawText(), jsonActual.GetRawText(), FailedOnPath, dbgPath);
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
