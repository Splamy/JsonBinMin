using System;

namespace JsonBinMin
{
	public class JBMOptions
	{
		public static readonly JBMOptions Default = new();

		// <1.0000> == <1.0>
		//public bool AllowSemanticallyEquivalentOpt { get; init; } = false;

		// <1.0> == <1>
		//public bool AllowReduceToIntegerOpt { get; init; } = false;

		public bool UseDict { get; init; } = true;

		public UseFloats UseFloats { get; init; } = UseFloats.Single | UseFloats.Double;

		public bool Compress { get; init; } = false;
	}

	[Flags]
	public enum UseFloats
	{
		None = 0,
		Half = 1 << 0,
		Single = 1 << 1,
		Double = 1 << 2,
	}
}
