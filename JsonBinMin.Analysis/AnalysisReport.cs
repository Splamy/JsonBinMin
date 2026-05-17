using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using JsonBinMin.BinV1;

namespace JsonBinMin.Analysis;

internal class AnalysisReport
{
	public int TotalTokenCount { get; set; }
	public int DictTokenCount { get; set; }
	public int DictElementsCount { get; set; }
	private Dictionary<JbmType, int> TypeCounts { get; } = [];
	// Bytelength -> Count
	private Dictionary<(JbmType NumType, int Length), int> IntegerCounts { get; } = [];

	public AnalysisReport()
	{
		TypeCounts[JbmType.Object] = 0;
		TypeCounts[JbmType.Array] = 0;
		TypeCounts[JbmType.String] = 0;

		IntegerCounts[(JbmType.Object, 1)] = 0;
		IntegerCounts[(JbmType.Array, 1)] = 0;
		IntegerCounts[(JbmType.String, 1)] = 0;
	}

	public void TrackType(JbmType type)
	{
		ref var cnt = ref CollectionsMarshal.GetValueRefOrAddDefault(TypeCounts, type, out _);
		cnt++;
	}

	public void TrackInteger(JbmType numType, int length)
	{
		ref var cnt = ref CollectionsMarshal.GetValueRefOrAddDefault(IntegerCounts, (numType, length), out _);
		cnt++;
	}

	public void WriteReport(StringBuilder sb)
	{
		sb.AppendLine("Total tokens: " + TotalTokenCount);
		sb.Append("Dict tokens: " + DictTokenCount)
			.Append(" (")
			.AppendFormat(CultureInfo.InvariantCulture, "{0:P2}", (double)DictTokenCount / TotalTokenCount)
			.Append(')')
			.AppendLine();
		sb.AppendLine("Dict elements: " + DictElementsCount);
		sb.AppendLine("Types:")
			.AppendLine(TypeCounts
				.OrderBy(x => x.Value)
				.Select(kvp => $"\t{FType(kvp.Key)}: {kvp.Value}")
				.Join("\n"));
		sb.AppendLine("Integers:")
			.AppendLine(IntegerCounts
				.OrderBy(x => x.Value)
				.Select(kvp => $"\t{FType(kvp.Key.NumType)}[{kvp.Key.Length}]: {kvp.Value}")
				.Join("\n"));

		AddInlineRatio("Object", IntegerCounts[(JbmType.Object, 1)], TypeCounts[JbmType.Object]);
		AddInlineRatio("Array", IntegerCounts[(JbmType.Array, 1)], TypeCounts[JbmType.Array]);
		AddInlineRatio("String", IntegerCounts[(JbmType.String, 1)], TypeCounts[JbmType.String]);

		void AddInlineRatio(string name, int inlined, int total)
		{
			sb.Append(name)
				.Append(" inlines: ")
				.Append(inlined)
				.Append('/')
				.Append(total)
				.Append(" (")
				.AppendFormat(CultureInfo.InvariantCulture, "{0:P2}", (double)inlined / total)
				.Append(')')
				.AppendLine();
		}
	}

	public static string FType(JbmType type) => type switch
	{
		JbmType.IntInline => "IntInline",


		JbmType.Object => "Object",
		JbmType.Array => "Array",
		JbmType.String => "String",

		JbmType.MetaDictDef => "MetaDictDef",
		JbmType.False => "False",
		JbmType.True => "True",
		JbmType.Null => "Null",

		JbmType.Float16 => "Float16",
		JbmType.Float32 => "Float32",
		JbmType.Float64 => "Float64",

		JbmType.Int8 => "Int8",
		JbmType.Int16 => "Int16",
		JbmType.Int24 => "Int24",
		JbmType.Int32 => "Int32",
		JbmType.Int48 => "Int48",
		JbmType.Int64 => "Int64",
		JbmType.IntRle => "IntRle",

		JbmType.NumStr => "NumStr",

		_ => type.ToString(),
	};

	public override string ToString()
	{
		var sb = new StringBuilder();
		WriteReport(sb);
		return sb.ToString();
	}
}

internal static class Ext
{
	public static string Join<T>(this IEnumerable<T> items, string separator) => string.Join(separator, items);
}