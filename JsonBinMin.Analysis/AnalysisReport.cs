using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace JsonBinMin.Analysis;

internal class AnalysisReport
{
	public int TotalTokenCount { get; set; }
	public int DictTokenCount { get; set; }
	public int DictElementsCount { get; set; }
	private Dictionary<JBMType, int> TypeCounts { get; } = [];
	// Bytelength -> Count
	private Dictionary<(JBMType NumType, int Length), int> IntegerCounts { get; } = [];

	public AnalysisReport()
	{
		TypeCounts[JBMType.Object] = 0;
		TypeCounts[JBMType.Array] = 0;
		TypeCounts[JBMType.String] = 0;

		IntegerCounts[(JBMType.Object, 1)] = 0;
		IntegerCounts[(JBMType.Array, 1)] = 0;
		IntegerCounts[(JBMType.String, 1)] = 0;
	}

	public void TrackType(JBMType type)
	{
		ref var cnt = ref CollectionsMarshal.GetValueRefOrAddDefault(TypeCounts, type, out _);
		cnt++;
	}

	public void TrackInteger(JBMType numType, int length)
	{
		ref var cnt = ref CollectionsMarshal.GetValueRefOrAddDefault(IntegerCounts, (numType, length), out _);
		cnt++;
	}

	public void WriteReport(StringBuilder sb)
	{
		sb.AppendLine("Total tokens: " + TotalTokenCount);
		sb.AppendLine("Dict tokens: " + DictTokenCount);
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

		AddInlineRatio("Object", IntegerCounts[(JBMType.Object, 1)], TypeCounts[JBMType.Object]);
		AddInlineRatio("Array", IntegerCounts[(JBMType.Array, 1)], TypeCounts[JBMType.Array]);
		AddInlineRatio("String", IntegerCounts[(JBMType.String, 1)], TypeCounts[JBMType.String]);

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

	public static string FType(JBMType type) => type switch
	{
		JBMType.IntInline => "IntInline",


		JBMType.Object => "Object",
		JBMType.Array => "Array",
		JBMType.String => "String",

		JBMType.MetaDictDef => "MetaDictDef",
		JBMType.False => "False",
		JBMType.True => "True",
		JBMType.Null => "Null",

		JBMType.Float16 => "Float16",
		JBMType.Float32 => "Float32",
		JBMType.Float64 => "Float64",

		JBMType.Int8 => "Int8",
		JBMType.Int16 => "Int16",
		JBMType.Int24 => "Int24",
		JBMType.Int32 => "Int32",
		JBMType.Int48 => "Int48",
		JBMType.Int64 => "Int64",
		JBMType.IntRle => "IntRle",

		JBMType.NumStr => "NumStr",

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