﻿using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Pointer;

[assembly: InternalsVisibleTo("JsonBinMin.Tests")]
[assembly: InternalsVisibleTo("JsonBinMin.Analysis")]

namespace JsonBinMin;

internal static class Util
{
	public static void Assert(bool assure)
	{
		if (!assure)
			Trace.Fail("Invariant error");
	}

	public static JsonObject GetOrCreate(JsonObject obj, JsonPointer ptr)
	{
		var cur = obj;
		foreach (var seg in ptr.Segments.Take(..^1))
		{
			cur = (JsonObject)cur.GetOrAdd(seg.Value, () => new JsonObject())!;
		}
		return cur;
	}

	public static TV GetOrAdd<TK, TV>(this IDictionary<TK, TV> dict, TK key, Func<TV> valueFactory)
	{
		if (!dict.TryGetValue(key, out var value))
		{
			value = valueFactory();
			dict[key] = value;
		}
		return value;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static JsonNode? ToJsonNode(this JsonElement element) => element.ValueKind switch
	{
		JsonValueKind.Array => JsonArray.Create(element),
		JsonValueKind.Object => JsonObject.Create(element),
		_ => JsonValue.Create(element)
	};

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsPrefixOf(this JsonPointer self, JsonPointer subKey)
	{
		if (subKey.Segments.Length >= self.Segments.Length)
			return false;

		for (var i = 0; i < subKey.Segments.Length; i++)
		{
			if (subKey.Segments[i] != self.Segments[i])
				return false;
		}

		return true;
	}
}
