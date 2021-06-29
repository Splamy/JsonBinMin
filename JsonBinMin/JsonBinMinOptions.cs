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

		public bool UseHalfType { get; init; } = false;
	}
}
