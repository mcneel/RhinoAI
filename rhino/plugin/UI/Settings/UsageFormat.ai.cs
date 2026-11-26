using System.Globalization;

namespace Rhino.AI.UI.Settings;

internal static class UsageFormat
{
    private static string[] Suffixes { get; } = ["K", "M", "B", "T"];

    private static double[] StepFactors { get; } = [1d, 2d, 5d, 10d];

    public static string Compact(long value, CultureInfo culture)
    {
        if (value < 1000)
            return value.ToString("N0", culture);

        double scaled = value;
        int unit = -1;
        do
        {
            scaled /= 1000d;
            unit++;
        }
        while (Math.Round(scaled, Decimals(scaled)) >= 1000d && unit < Suffixes.Length - 1);

        return scaled.ToString(Decimals(scaled) == 1 ? "0.#" : "0", culture) + Suffixes[unit];
    }

    public static string Cost(decimal dollars, CultureInfo culture)
        => dollars > 0m && dollars < 0.01m
            ? "<$" + 0.01m.ToString("N2", culture)
            : "$" + dollars.ToString("N2", culture);

    public static (double Max, double Step) NiceScale(long peak, int ticks)
    {
        if (peak <= 0)
            return (ticks, 1d);

        double rough = (double)peak / ticks;
        double magnitude = Math.Pow(10d, Math.Floor(Math.Log10(rough)));
        double step = Math.Max(1d, StepFactors.Select(factor => factor * magnitude).First(candidate => candidate >= rough));
        return (Math.Ceiling(peak / step) * step, step);
    }

    private static int Decimals(double scaled) => scaled < 99.95d ? 1 : 0;
}
