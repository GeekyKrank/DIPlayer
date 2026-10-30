using NAudio.Dsp;
using NAudio.Wave;

namespace DIPlayer.Audio;

/// <summary>Six independent peaking filters per channel with audio-thread-only DSP state.</summary>
public sealed class EqualizerSampleProvider : ISampleProvider
{
    public const int BandCount = 6;
    public static IReadOnlyList<float> Frequencies { get; } = Array.AsReadOnly(new float[] { 60, 170, 350, 1000, 3500, 10000 });
    sealed record Configuration(bool Enabled, double[] Gains, double HeadroomDb);
    readonly ISampleProvider source;
    readonly BiQuadFilter[][] filters;
    readonly double[] currentGains = new double[BandCount];
    Configuration configuration = new(false, new double[BandCount], 0);
    bool active;
    double wetMix, currentHeadroomDb;
    int channel;

    public WaveFormat WaveFormat => source.WaveFormat;

    public EqualizerSampleProvider(ISampleProvider source)
    {
        ArgumentNullException.ThrowIfNull(source);
        this.source = source;
        filters = new BiQuadFilter[source.WaveFormat.Channels][];
        for (int i = 0; i < filters.Length; i++) ResetChannel(i);
    }

    public static double[] NormalizeGains(IReadOnlyList<double>? gains)
    {
        var result = new double[BandCount];
        for (int i = 0; i < result.Length; i++)
            if (gains != null && i < gains.Count && double.IsFinite(gains[i])) result[i] = Math.Clamp(gains[i], -12, 12);
        return result;
    }

    /// <summary>Publish an immutable configuration; no UI thread touches running filter state.</summary>
    public void SetEqualizer(bool enabled, double[]? gains, bool headroom = false)
    {
        var normalized = NormalizeGains(gains);
        double preamp = headroom && enabled ? EqualizerMath.HeadroomDb(normalized, WaveFormat.SampleRate) : 0;
        Volatile.Write(ref configuration, new Configuration(enabled, normalized, preamp));
    }

    void ResetChannel(int index)
    {
        filters[index] = new BiQuadFilter[BandCount];
        for (int band = 0; band < BandCount; band++)
            filters[index][band] = BiQuadFilter.PeakingEQ(WaveFormat.SampleRate,
                Math.Min(Frequencies[band], WaveFormat.SampleRate * .45f), 1, (float)currentGains[band]);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        var next = Volatile.Read(ref configuration);
        bool apply = next.Enabled && next.Gains.Any(gain => gain != 0);
        if (!apply && !active)
        {
            // Native bypass is bit-for-bit transparent and does no filter work.
            channel = (channel + read) % filters.Length;
            return read;
        }
        if (!active)
        {
            Array.Copy(next.Gains, currentGains, BandCount);
            for (int i = 0; i < filters.Length; i++) ResetChannel(i);
            currentHeadroomDb = next.HeadroomDb;
            wetMix = 0;
            active = true;
        }
        int end = offset + read;
        for (int start = offset; start < end;)
        {
            int chunk = Math.Min(filters.Length * 64, end - start);
            // Smooth live knob movements over roughly 20 ms. Retune coefficients
            // on this audio thread while retaining each channel's filter history.
            double blend = 1 - Math.Exp(-Math.Max(1, chunk / filters.Length) / (WaveFormat.SampleRate * .02));
            for (int band = 0; apply && band < BandCount; band++)
            {
                if (currentGains[band] == next.Gains[band]) continue;
                currentGains[band] += (next.Gains[band] - currentGains[band]) * blend;
                if (Math.Abs(currentGains[band] - next.Gains[band]) < .0001) currentGains[band] = next.Gains[band];
                for (int c = 0; c < filters.Length; c++)
                    filters[c][band].SetPeakingEq(WaveFormat.SampleRate,
                        Math.Min(Frequencies[band], WaveFormat.SampleRate * .45f), 1, (float)currentGains[band]);
            }
            if (apply)
            {
                // Reduce headroom before a new boost ramps in; release it gently
                // when the response becomes quieter.
                currentHeadroomDb = next.HeadroomDb < currentHeadroomDb ? next.HeadroomDb
                    : currentHeadroomDb + (next.HeadroomDb - currentHeadroomDb) * blend;
            }
            double preamp = Math.Pow(10, currentHeadroomDb / 20);
            double fadeStep = 1 / (WaveFormat.SampleRate * .02), targetWet = apply ? 1 : 0;
            for (int i = start; i < start + chunk; i++)
            {
                if (channel == 0) wetMix += Math.Clamp(targetWet - wetMix, -fadeStep, fadeStep);
                if (wetMix == 0)
                {
                    channel = (channel + 1) % filters.Length;
                    continue;
                }
                float value = float.IsFinite(buffer[i]) ? buffer[i] : 0;
                float dry = value;
                for (int band = 0; band < BandCount; band++) value = filters[channel][band].Transform(value);
                if (!float.IsFinite(value)) { ResetChannel(channel); value = 0; }
                buffer[i] = (float)Math.Clamp(dry + wetMix * (value * preamp - dry), -1, 1);
                channel = (channel + 1) % filters.Length;
            }
            start += chunk;
        }
        if (!apply && wetMix == 0) active = false;
        return read;
    }
}
