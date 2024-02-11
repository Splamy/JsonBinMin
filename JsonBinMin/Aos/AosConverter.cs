using System.Text.Json;
using System.Text.Json.Nodes;
using Json.More;
using Json.Pointer;

namespace JsonBinMin.Aos;

public static class AosConverter
{
	public static void FindCompressibleArrays(AosData<JsonArray> aosData, JsonNode node, JsonObject? parent, string key)
	{
		if (node is JsonArray arr)
		{
			// Can't opmize arrays which are not on a object field
			if (parent is null)
			{
				return;
			}

			// Ignore small arrays
			if (arr.Count < 32)
			{
				return;
			}

			EncodeArray(aosData, parent, key, arr);
		}
		else if (node is JsonObject obj)
		{
			foreach (var kvp in obj.ToArray())
			{
				if (kvp.Value is null)
				{
					continue;
				}

				FindCompressibleArrays(aosData, kvp.Value, obj, kvp.Key);
			}
		}
	}

	public static Dictionary<string, JsonNode?> FlattenNode(JsonObject node)
	{
		var flatObj = new Dictionary<string, JsonNode?>();
		FlattenNodeRec(flatObj, JsonPointer.Empty, node);
		return flatObj;

		static void FlattenNodeRec(Dictionary<string, JsonNode?> flatObj, JsonPointer ptr, JsonObject node)
		{
			foreach (var kvp in node)
			{
				if (kvp.Value is null)
				{
					flatObj[kvp.Key] = null;
					continue;
				}

				var flatKey = ptr.Combine(PointerSegment.Create(kvp.Key));

				if (kvp.Value is JsonObject obj)
				{
					FlattenNodeRec(flatObj, flatKey, obj);
				}
				else
				{
					flatObj[flatKey.ToString()] = kvp.Value.DeepClone();
				}
			}
		}
	}

	public static JsonObject UnflattenNode(Dictionary<string, JsonNode?> node)
	{
		var unflatObj = new JsonObject();

		foreach (var kvp in node)
		{
			if (kvp.Value is null)
			{
				unflatObj[kvp.Key] = null;
				continue;
			}

			var parts = JsonPointer.Parse(kvp.Key).Segments.Select(x => x.Value).ToArray();
			var last = parts.Length - 1;
			var cur = unflatObj;

			for (int i = 0; i < last; i++)
			{
				var part = parts[i];
				cur = cur.GetOrAdd(part, () => new JsonObject()).AsObject();
			}

			cur[parts[last]] = kvp.Value.DeepClone();
		}

		return unflatObj;
	}

	public static void EncodeArray(AosData<JsonArray> aosData, JsonObject parent, string key, JsonArray array)
	{
		// Must be an array of objects
		if (!array.All(x => x is JsonObject))
		{
			return;
		}

		var flatObjs = array.Cast<JsonObject>().Select(FlattenNode).ToList();

		var allKeys = flatObjs.SelectMany(x => x.Select(x => x.Key)).ToHashSet();
		var optKeys = allKeys
			.Select(kkey => {
				bool hasNull = false;
				int cnt = 0;

				foreach (var flatObj in flatObjs)
				{
					if (flatObj.TryGetValue(kkey, out var node))
					{
						cnt++;
						hasNull = hasNull || node is null;
					}
				}

				var opt = ArrOpt.None;
				if (cnt == flatObjs.Count)
				{
					opt = ArrOpt.All;
				}
				else if (cnt > flatObjs.Count / 2 && !hasNull)
				{
					opt = ArrOpt.NonNull;
				}

				return (kkey, opt);
			})
			.ToArray();

		if (optKeys.All(x => x.opt == ArrOpt.None))
		{
			return;
		}

		var ptrToArr = JsonPointer.Parse(array.GetPointerFromRoot());

		foreach (var (optKey, optKind) in optKeys)
		{
			if (optKind == ArrOpt.None)
			{
				continue;
			}

			var arr = new JsonArray();
			foreach (var flatObj in flatObjs)
			{
				if (!flatObj.Remove(optKey, out var val))
				{
					if (optKind == ArrOpt.All)
					{
						throw new InvalidOperationException();
					}
				}
				arr.Add(val);
			}

			var emptyType = optKind switch
			{
				ArrOpt.None => throw new InvalidOperationException(),
				ArrOpt.All => JsonValueKind.Undefined,
				ArrOpt.NonNull => JsonValueKind.Null,
			};

			aosData.Aos.GetOrAdd(ptrToArr.ToString(), () => new()).Add(optKey, new(emptyType, arr));
		}

		var unflatObjs = flatObjs.Select(UnflattenNode).ToArray();

		if (unflatObjs.All(x => x.AsObject().Count == 0))
		{
			parent.Remove(key);
		}
		else
		{
			parent[key] = new JsonArray(unflatObjs);
		}
	}

	public static JsonNode? Encode(JsonNode? node)
	{
		if (node is null)
		{
			return null;
		}

		var clone = node.DeepClone();
		if (clone is not JsonObject joClone)
		{
			return clone;
		}

		var aos = new AosData<JsonArray>()
		{
			Data = joClone,
		};
		FindCompressibleArrays(aos, joClone, null, "");
		return JsonSerializer.SerializeToNode(aos)!;
	}

	public static JsonNode Decode(AosData<JsonElement> aosDeser)
	{
		foreach (var aosArr in aosDeser.Aos)
		{
			var insertPtr = JsonPointer.Parse(aosArr.Key);
			var parentObj = Util.GetOrCreate(aosDeser.Data, insertPtr);
			var parentArr = parentObj.GetOrAdd(insertPtr.Segments.Last().Value, () => new JsonArray()).AsArray();

			foreach (var aosField in aosArr.Value)
			{
				var len = aosField.Value.A.GetArrayLength();
				for (int ci = parentArr.Count; ci <= len; ci++)
				{
					parentArr.Add(new JsonObject());
				}

				var fldPtr = JsonPointer.Parse(aosField.Key);

				int i = 0;
				var nullType = aosField.Value.N;
				foreach (var elem in aosField.Value.A.EnumerateArray())
				{
					if (elem.ValueKind != nullType)
					{
						var arrElemObj = Util.GetOrCreate(parentArr[i].AsObject(), fldPtr);
						arrElemObj[fldPtr.Segments.Last().Value] = elem.ToJsonNode();
					}
					i++;
				}
			}
		}

		return aosDeser.Data;
	}
}
