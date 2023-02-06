using System.Diagnostics;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("JsonBinMin.Tests")]

namespace JsonBinMin;

internal static class Util
{
	public static void Assert(bool assure)
	{
		if (!assure)
			Trace.Fail("Invariant error");
	}
}
