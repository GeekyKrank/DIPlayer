using DIPlayer.Audio;
using NAudio.Wave;

static class EqualizerChecks
{
    sealed class SineSource(double frequency, int channels = 1, int sampleRate = 48000, bool silentRight = false, double amplitude = .025) : ISampleProvider
    {
        long position;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        public int Read(float[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++, position++)
                buffer[offset+i] = silentRight && position%channels!=0 ? 0 : (float)(amplitude*Math.Sin(2*Math.PI*frequency*(position/channels)/sampleRate));
            return count;
        }
    }
    sealed class ArraySource(float[] data, int channels = 2) : ISampleProvider
    {
        int position;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, channels);
        public int Read(float[] buffer, int offset, int count)
        {
            int read=Math.Min(count,data.Length-position);
            Array.Copy(data,position,buffer,offset,read);position+=read;
            return read;
        }
    }
    static void Check(bool condition, string label)
    {
        if(!condition)throw new Exception("Equalizer: "+label);
        Console.WriteLine("PASS equalizer "+label);
    }
    static double Measure(ISampleProvider source)
    {
        var buffer=new float[source.WaveFormat.SampleRate];
        source.Read(buffer,0,buffer.Length);
        // Ignore startup and coefficient-smoothing transients.
        return Math.Sqrt(buffer.Skip(buffer.Length/2).Average(value=>(double)value*value));
    }
    public static async Task Run()
    {
        float[] meterPcm=[0,.25f,0,-.5f,0,.75f,0,-.125f];
        var meter=new PlaybackMeter(new ArraySource(meterPcm));
        Check(meter.Energy==0,"visual meter begins with zero energy");
        var meterOutput=Enumerable.Repeat(123f,meterPcm.Length+6).ToArray();
        int meterRead=meter.Read(meterOutput,3,meterPcm.Length);
        Check(meterRead==meterPcm.Length&&meterOutput.Skip(3).Take(meterRead).SequenceEqual(meterPcm)&&meterOutput.Take(3).All(value=>value==123)&&meterOutput.Skip(3+meterRead).All(value=>value==123),"visual meter preserves every PCM sample and nonzero buffer boundaries");
        Check(meter.Energy>0&&meter.Energy<=1,"visual meter detects right-channel audio with a silent left channel");
        meter.Read(meterOutput,3,meterPcm.Length);
        Check(meter.Energy==0,"visual meter resets energy at end of stream");
        var silentMeter=new PlaybackMeter(new ArraySource(new float[32]));
        silentMeter.Read(new float[40],4,32);
        Check(silentMeter.Energy==0,"silent audio produces zero visual energy");
        float[] original=Enumerable.Range(0,8193).Select(i=>(float)(Math.Sin(i*.37)*.4)).ToArray();
        foreach(bool enabled in new[]{false,true})
        {
            var provider=new EqualizerSampleProvider(new ArraySource(original));
            provider.SetEqualizer(enabled,enabled?new double[6]:[12,-12,12,-12,12,-12]);
            var output=new float[original.Length+6];Array.Fill(output,123);
            int read=provider.Read(output,3,original.Length);
            Check(read==original.Length&&output.Skip(3).Take(read).SequenceEqual(original)&&output[0]==123&&output[^1]==123,
                enabled?"neutral settings are bit-for-bit transparent":"disabled settings bypass all filters and preserve buffer bounds");
        }
        foreach(float frequency in EqualizerSampleProvider.Frequencies)
        {
            int band=EqualizerSampleProvider.Frequencies.ToList().IndexOf(frequency);
            double baseline=Measure(new SineSource(frequency));
            foreach(double gain in new[]{6d,-6d})
            {
                var provider=new EqualizerSampleProvider(new SineSource(frequency));
                var gains=new double[6];gains[band]=gain;
                provider.SetEqualizer(true,gains);
                double actual=20*Math.Log10(Measure(provider)/baseline);
                Check(Math.Abs(actual-gain)<.25,$"{frequency} Hz peak measures {gain:+0;-0} dB (actual {actual:0.00} dB)");
                Check(Math.Abs(EqualizerMath.ResponseDb(frequency,gains)-actual)<.25,$"{frequency} Hz analytical response matches audio output");
            }
        }
        var stereo=new EqualizerSampleProvider(new SineSource(350,2,silentRight:true));
        stereo.SetEqualizer(true,[12,12,12,12,12,12]);
        var oddBuffer=new float[133];long index=0;double energy=0;
        for(int block=0;block<50;block++)
        {
            int read=stereo.Read(oddBuffer,3,127);
            for(int i=0;i<read;i++,index++)
            {
                if(index%2==1)CheckSilent(oddBuffer[i+3]);
                else energy+=oddBuffer[i+3]*oddBuffer[i+3];
            }
        }
        Check(energy>0,"channels stay independent across odd-sized reads and nonzero offsets");

        var live=new EqualizerSampleProvider(new SineSource(1000));
        double before=Measure(live);
        var published=new double[]{0,0,0,6,0,0};
        live.SetEqualizer(true,published);published[3]=-12;
        double boosted=Measure(live);
        Check(Math.Abs(20*Math.Log10(boosted/before)-6)<.25,"live gain publication is copied and applies without restarting the source");
        live.SetEqualizer(false,[0,0,0,6,0,0]);
        Check(Math.Abs(Measure(live)/before-1)<.0001,"live disabling restores unity gain");

        var fading=new EqualizerSampleProvider(new SineSource(1000));
        fading.SetEqualizer(true,[0,0,0,6,0,0]);
        Measure(fading);
        fading.SetEqualizer(false,[0,0,0,6,0,0]);
        var transition=new float[96];fading.Read(transition,0,transition.Length);
        double transitionRms=Math.Sqrt(transition.Average(value=>(double)value*value));
        Check(transitionRms>before*1.5,"bypass fades from the running response instead of switching abruptly");
        var bypassData=Enumerable.Range(0,9000).Select(i=>(float)(.4*Math.Sin(i*.17))).ToArray();
        var settling=new EqualizerSampleProvider(new ArraySource(bypassData,1));
        settling.SetEqualizer(true,[0,0,0,6,0,0]);
        var settlingBuffer=new float[4096];settling.Read(settlingBuffer,0,settlingBuffer.Length);
        settling.SetEqualizer(false,[0,0,0,6,0,0]);
        settling.Read(settlingBuffer,0,2048);
        var transparent=new float[256];settling.Read(transparent,0,transparent.Length);
        Check(transparent.SequenceEqual(bypassData.Skip(6144).Take(256)),"settled bypass returns to bit-for-bit transparent output");

        var protectedTone=new EqualizerSampleProvider(new SineSource(1000));
        protectedTone.SetEqualizer(true,[0,0,0,6,0,0],headroom:true);
        double protectedDb=20*Math.Log10(Measure(protectedTone)/before);
        Check(Math.Abs(protectedDb+.5)<.25,"automatic headroom compensates a boost and keeps a half-decibel margin");
        Check(EqualizerMath.HeadroomDb([-6,-6,-6,-6,-6,-6])==0,"cut-only settings do not reduce the overall level");
        double[] maximumBoost=[12,12,12,12,12,12];
        var protectedCurve=EqualizerMath.Curve(maximumBoost,headroom:true,points:512);
        Check(protectedCurve.All(point=>double.IsFinite(point.Decibels)&&point.Decibels<=-.45),"combined-band headroom covers overlapping peaks across the response");
        var loud=new EqualizerSampleProvider(new SineSource(1000,amplitude:.95));
        loud.SetEqualizer(true,maximumBoost,headroom:true);
        var loudBuffer=new float[48000];loud.Read(loudBuffer,0,loudBuffer.Length);
        Check(loudBuffer.Skip(24000).Max(value=>Math.Abs(value))<.95,"automatic headroom prevents steady boosted full-scale tones from clipping");
        live.SetEqualizer(true,[double.NaN,double.PositiveInfinity,-100,100,12,-12]);
        var extremes=new float[4096];
        live.Read(extremes,0,extremes.Length);
        Check(extremes.All(value=>float.IsFinite(value)&&Math.Abs(value)<=1),"invalid and extreme gains remain finite and bounded");
        Check(EqualizerSampleProvider.NormalizeGains([double.NaN,99,-99]).SequenceEqual(new double[]{0,12,-12,0,0,0}),"saved gains are normalized to six safe bands");
        var lowRate=new EqualizerSampleProvider(new SineSource(1000,sampleRate:8000));
        lowRate.SetEqualizer(true,[12,-12,12,-12,12,-12]);
        lowRate.Read(extremes,0,extremes.Length);
        Check(extremes.All(float.IsFinite),"bands above Nyquist are safely bounded for low-rate streams");

        var concurrent=new EqualizerSampleProvider(new SineSource(170,2));
        var changing=Task.Run(()=>
        {
            for(int i=0;i<2000;i++)concurrent.SetEqualizer(i%3!=0,[i%25-12,12-i%25,6,-6,12,-12]);
        });
        for(int i=0;i<200;i++)
        {
            concurrent.Read(extremes,0,extremes.Length);
            if(!extremes.All(float.IsFinite))throw new Exception("Concurrent equalizer change corrupted DSP state");
        }
        await changing;
        Check(true,"concurrent UI updates do not mutate audio-thread filter state");
    }
    static void CheckSilent(float value)
    {
        if(value!=0)throw new Exception("Equalizer leaked left channel history into silent right channel");
    }
}
