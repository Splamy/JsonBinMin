using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.More;
using Json.Pointer;

namespace JsonBinMin.Aos;

public static class AosConverter
{
	public static void FindCompressibleArrays(AosData<JsonArray> aosData, JsonNode node, JsonObject? parent, string key, JBMOptions options)
	{
		if (node is JsonArray arr)
		{
			// Can't opmize arrays which are not on a object field
			if (parent is null)
			{
				return;
			}

			// Ignore small arrays
			if (arr.Count < options.AosMinArraySize)
			{
				return;
			}

			var newArr = EncodeArray(aosData, arr, options);
			if (newArr == null)
			{
				parent.Remove(key);
			}
			else if (newArr != arr)
			{
				parent[key] = newArr;
			}
		}
		else if (node is JsonObject obj)
		{
			foreach (var kvp in obj.ToArray())
			{
				if (kvp.Value is null)
				{
					continue;
				}

				FindCompressibleArrays(aosData, kvp.Value, obj, kvp.Key, options);
			}
		}
	}

	public static Dictionary<JsonPointer, JsonNode?> FlattenNode(JsonObject node)
	{
		var flatObj = new Dictionary<JsonPointer, JsonNode?>();
		FlattenNodeRec(flatObj, JsonPointer.Empty, node);
		return flatObj;

		static void FlattenNodeRec(Dictionary<JsonPointer, JsonNode?> flatObj, JsonPointer ptr, JsonObject node)
		{
			foreach (var kvp in node)
			{
				var flatKey = ptr.Combine(kvp.Key);

				if (kvp.Value is null)
				{
					flatObj[flatKey] = null;
					continue;
				}

				if (kvp.Value is JsonObject obj)
				{
					if (obj.Count == 0)
					{
						flatObj[flatKey] = new JsonObject();
					}
					else
					{
						FlattenNodeRec(flatObj, flatKey, obj);
					}
				}
				else
				{
					flatObj[flatKey] = kvp.Value.DeepClone();
				}
			}
		}
	}

	public static JsonObject UnflattenNode(Dictionary<JsonPointer, JsonNode?> node)
	{
		var unflatObj = new JsonObject();

		foreach (var kvp in node)
		{
			var last = kvp.Key.Segments.Length - 1;
			var cur = unflatObj;

			for (int i = 0; i < last; i++)
			{
				var part = kvp.Key.Segments[i].Value;
				cur = cur.GetOrAdd(part, () => new JsonObject()).AsObject();
			}

			cur[kvp.Key.Segments[last].Value] = kvp.Value?.DeepClone();
		}

		return unflatObj;
	}

	private static Dictionary<JsonPointer, KeyBuildData> AnalyzeKeys(JsonArray elem)
	{
		Dictionary<JsonPointer, KeyBuildData> keys = [];
		foreach (var obj in elem.Cast<JsonObject>())
		{
			AnanlyzeKeysRec(keys, JsonPointer.Empty, obj);
		}
		return keys;

		static void AnanlyzeKeysRec(Dictionary<JsonPointer, KeyBuildData> keys, JsonPointer ptr, JsonObject elem)
		{
			foreach (var kvp in elem)
			{
				var flatKey = ptr.Combine(kvp.Key);

				ref var kbd = ref CollectionsMarshal.GetValueRefOrAddDefault(keys, flatKey, out _);
				kbd.InclCount++;

				if (kvp.Value is JsonObject jo)
				{
					if (jo.Count == 0)
					{
						kbd.ExclCount++;
					}
					else
					{
						AnanlyzeKeysRec(keys, flatKey, jo);
					}
				}
				else
				{
					kbd.ExclCount++;

					if (kvp.Value == null)
					{
						kbd.HasNull = true;
					}
					else if (kvp.Value.GetValueKind() == JsonValueKind.Number)
					{
						kbd.HasNum = true;
					}
				}
			}
		}
	}

	private struct KeyBuildData
	{
		public int InclCount;
		public int ExclCount;
		public bool HasNull;
		public bool HasNum;
	}

	public static JsonArray? EncodeArray(AosData<JsonArray> aosData, JsonArray array, JBMOptions options)
	{
		// Must be an array of objects
		if (!array.All(x => x is JsonObject))
		{
			return array;
		}

		var flatObjs = array.Cast<JsonObject>().Select(FlattenNode).ToList();

		var allKeys = flatObjs.SelectMany(x => x.Select(x => x.Key)).ToHashSet();
		var optKeys = allKeys
			.Select(kkey => {
				int cnt = 0;
				bool hasNull = false;
				bool hasNum = false;

				foreach (var flatObj in flatObjs)
				{
					if (flatObj.TryGetValue(kkey, out var node))
					{
						cnt++;
						if (node is null)
						{
							hasNull = true;
						}
						else if (node.GetValueKind() == JsonValueKind.Number)
						{
							hasNum = true;
						}
					}
				}

				var opt = ArrOpt.None;
				if (cnt == flatObjs.Count)
				{
					opt = ArrOpt.All;
				}
				else if (cnt > flatObjs.Count / options.AosMinSparseFraction)
				{
					if (!hasNull)
					{
						opt = ArrOpt.SkipNull;
					}
					else if (!hasNum)
					{
						opt = ArrOpt.SkipNum;
					}
				}

				return new KeyData(kkey, opt);
			})
			.ToArray();

		if (optKeys.All(x => x.Opt == ArrOpt.None))
		{
			return array;
		}

		var ptrToArr = JsonPointer.Parse(array.GetPointerFromRoot());

		foreach (var (optKey, optKind) in optKeys)
		{
			if (optKind == ArrOpt.None)
			{
				continue;
			}

			// If all elements are just a empty restore object or nonexistant we can skip this array
			if (flatObjs.All(x => !x.TryGetValue(optKey, out var xval) || xval is JsonObject { Count: 0 }))
			{
				continue;
			}

			var emptyType = optKind switch
			{
				ArrOpt.None => throw new InvalidOperationException(),
				ArrOpt.All => JsonValueKind.Undefined,
				ArrOpt.SkipNull => JsonValueKind.Null,
				ArrOpt.SkipNum => JsonValueKind.Number,
			};

			Func<JsonNode?> genNullFn = optKind switch
			{
				ArrOpt.None => throw new InvalidOperationException(),
				ArrOpt.All => () => throw new InvalidOperationException("Type All should not have empty slots"),
				ArrOpt.SkipNull => () => null,
				ArrOpt.SkipNum => () => new JsonObject(),
			};

			var arr = new JsonArray();
			foreach (var flatObj in flatObjs)
			{
				if (flatObj.Remove(optKey, out var val))
				{
					arr.Add(val);
				}
				else
				{
					arr.Add(genNullFn());
				}
			}

			aosData.Aos.GetOrAdd(ptrToArr.ToString(), () => new()).Add(optKey.ToString(), new(emptyType, arr));
		}

		var unflatObjs = flatObjs.Select(UnflattenNode).ToArray();

		if (unflatObjs.All(x => x.AsObject().Count == 0))
		{
			return null;
		}
		else
		{
			return new JsonArray(unflatObjs);
		}
	}


	public static JsonNode? Encode(JsonNode? node, JBMOptions options)
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
		FindCompressibleArrays(aos, joClone, null, "", options);
		return JsonSerializer.SerializeToNode(aos)!;
	}

	public static JsonNode Decode(AosData<JsonElement> aosDeser, JBMOptions options)
	{
		foreach (var aosArr in aosDeser.Aos)
		{
			var insertPtr = JsonPointer.Parse(aosArr.Key);
			var parentObj = Util.GetOrCreate(aosDeser.Data, insertPtr);
			var parentArr = parentObj.GetOrAdd(insertPtr.Segments.Last().Value, () => new JsonArray()).AsArray();

			foreach (var aosField in aosArr.Value)
			{
				var len = aosField.Value.A.GetArrayLength();
				for (int ci = parentArr.Count; ci < len; ci++)
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

	private enum ArrOpt
	{
		None = 0,
		All = 1,
		SkipNull = 2,
		SkipNum = 3,
		SythObj = 4,
	}

	private record struct KeyData(JsonPointer Key, ArrOpt Opt);
}
