namespace DIPlayer.Audio;

/// <summary>The response of the same Q=1 peaking biquads used by the audio pipeline.</summary>
public static class EqualizerMath
{
    readonly record struct Coefficients(double B0, double B1, double B2, double A0, double A1, double A2);

    static Coefficients[] Build(IReadOnlyList<double>? gains, int sampleRate)
    {
        var normalized = EqualizerSampleProvider.NormalizeGains(gains);
        var filters = new Coefficients[EqualizerSampleProvider.BandCount];
        for (int band = 0; band < filters.Length; band++)
        {
            double omega = 2 * Math.PI * Math.Min(EqualizerSampleProvider.Frequencies[band], sampleRate * .45) / sampleRate;
            double amplitude = Math.Pow(10, normalized[band] / 40), alpha = Math.Sin(omega) / 2, cosine = -2 * Math.Cos(omega);
            filters[band] = new(1 + alpha * amplitude, cosine, 1 - alpha * amplitude,
                1 + alpha / amplitude, cosine, 1 - alpha / amplitude);
        }
        return filters;
    }

    static double Response(double frequency, Coefficients[] filters, int sampleRate)
    {
        double omega = 2 * Math.PI * Math.Clamp(frequency, 1, sampleRate * .499) / sampleRate;
        double c1 = Math.Cos(omega), s1 = Math.Sin(omega), c2 = Math.Cos(2 * omega), s2 = Math.Sin(2 * omega), decibels = 0;
        foreach (var filter in filters)
        {
            double numeratorReal = filter.B0 + filter.B1 * c1 + filter.B2 * c2;
            double numeratorImaginary = filter.B1 * s1 + filter.B2 * s2;
            double denominatorReal = filter.A0 + filter.A1 * c1 + filter.A2 * c2;
            double denominatorImaginary = filter.A1 * s1 + filter.A2 * s2;
            double numerator = numeratorReal * numeratorReal + numeratorImaginary * numeratorImaginary;
            double denominator = denominatorReal * denominatorReal + denominatorImaginary * denominatorImaginary;
            decibels += 10 * Math.Log10(Math.Max(1e-30, numerator) / Math.Max(1e-30, denominator));
        }
        return decibels;
    }

    public static double ResponseDb(double frequency, IReadOnlyList<double>? gains, int sampleRate = 48000) =>
        Response(frequency, Build(gains, Math.Max(1000, sampleRate)), Math.Max(1000, sampleRate));

    public static double HeadroomDb(IReadOnlyList<double>? gains, int sampleRate = 48000)
    {
        sampleRate = Math.Max(1000, sampleRate);
        var filters = Build(gains, sampleRate);
        double upper = Math.Min(22000, sampleRate * .49), peak = 0;
        for (int point = 0; point <= 384; point++)
            peak = Math.Max(peak, Response(20 * Math.Pow(upper / 20, point / 384d), filters, sampleRate));
        foreach (float frequency in EqualizerSampleProvider.Frequencies)
            peak = Math.Max(peak, Response(Math.Min(frequency, sampleRate * .45), filters, sampleRate));
        return peak > .01 ? -peak - .5 : 0;
    }

    public static (double Frequency, double Decibels)[] Curve(IReadOnlyList<double>? gains, bool headroom = false, int points = 160, int sampleRate = 48000)
    {
        sampleRate = Math.Max(1000, sampleRate);
        var filters = Build(gains, sampleRate);
        double preamp = headroom ? HeadroomDb(gains, sampleRate) : 0, upper = Math.Min(20000, sampleRate * .49);
        var result = new (double Frequency, double Decibels)[Math.Clamp(points, 16, 512)];
        for (int point = 0; point < result.Length; point++)
        {
            double frequency = 20 * Math.Pow(upper / 20, point / (double)(result.Length - 1));
            result[point] = (frequency, Response(frequency, filters, sampleRate) + preamp);
        }
        return result;
    }
}
