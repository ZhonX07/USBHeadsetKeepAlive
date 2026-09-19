using NAudio.Wave;

namespace UsbHeadsetKeepAlive;

/// <summary>
/// Infinite, very-low-level stereo sine source. A real non-zero waveform is used
/// because many DACs detect and suppress digital silence (all-zero samples).
/// </summary>
internal sealed class KeepAliveWaveProvider : IWaveProvider
{
    private const double Frequency = 440.0;
    private double phase;
    private volatile float amplitude;

    public KeepAliveWaveProvider(int sampleRate = 48_000, int channels = 2)
    {
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
    }

    public WaveFormat WaveFormat { get; }

    public void SetLevelDb(double decibels)
    {
        amplitude = (float)Math.Pow(10.0, decibels / 20.0);
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        int bytesPerSample = sizeof(float);
        int sampleCount = count / bytesPerSample;
        int channels = WaveFormat.Channels;
        float currentAmplitude = amplitude;
        double phaseIncrement = 2.0 * Math.PI * Frequency / WaveFormat.SampleRate;

        for (int sample = 0; sample < sampleCount; sample += channels)
        {
            float value = (float)(Math.Sin(phase) * currentAmplitude);
            phase += phaseIncrement;
            if (phase >= 2.0 * Math.PI)
                phase -= 2.0 * Math.PI;

            for (int channel = 0; channel < channels && sample + channel < sampleCount; channel++)
            {
                BitConverter.TryWriteBytes(
                    buffer.AsSpan(offset + (sample + channel) * bytesPerSample, bytesPerSample),
                    value);
            }
        }

        return count;
    }
}
