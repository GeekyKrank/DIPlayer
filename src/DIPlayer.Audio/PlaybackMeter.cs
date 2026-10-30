using NAudio.Wave;

namespace DIPlayer.Audio;

/// <summary>Read-only visual envelope; samples pass through unchanged.</summary>
public sealed class PlaybackMeter(ISampleProvider source) : ISampleProvider
{
    float energy;
    long readAt;
    public WaveFormat WaveFormat => source.WaveFormat;
    public float Energy => Environment.TickCount64 - Interlocked.Read(ref readAt) < 300 ? Volatile.Read(ref energy) : 0;
    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        double sum = 0;
        for (int i = offset; i < offset + read; i++)
        {
            double sample = buffer[i];
            if (double.IsFinite(sample)) sum += sample * sample;
        }
        Volatile.Write(ref energy, read > 0 ? (float)Math.Clamp(Math.Sqrt(sum / read) * 3.2, 0, 1) : 0);
        Interlocked.Exchange(ref readAt, Environment.TickCount64);
        return read;
    }
}
