using System.Diagnostics;
using System.Runtime.CompilerServices;

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
}
