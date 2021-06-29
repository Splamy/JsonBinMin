using System.Diagnostics;

namespace JsonBinMin
{
	internal static class Util
	{
		public static void Assert(bool assure)
		{
			if (!assure)
				Trace.Fail("Invariant error");
		}
	}
}
