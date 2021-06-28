namespace JsonBinMin
{
	public class JsonBinMinOptions
	{
		public static readonly JsonBinMinOptions Default = new();

		// <1.0000> == <1.0>
		//public bool AllowSemanticallyEquivalentOpt { get; init; } = false;

		// <1.0> == <1>
		//public bool AllowReduceToIntegerOpt { get; init; } = false;

		public bool UseDict { get; init; } = true;

		public bool UseHalfType { get; init; } = false;
	}
}
