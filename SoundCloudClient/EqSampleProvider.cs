using NAudio.Dsp;
using NAudio.Wave;
using System;

namespace SoundCloudClient
{
    public class EqSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly BiQuadFilter[] _filters;
        private readonly int _channels;

        // 10 полос: 32 Hz — 16 kHz
        public static readonly (string Name, double Frequency, double Bandwidth)[] Bands =
        {
            ("32", 32, 1.5),
            ("64", 64, 1.5),
            ("125", 125, 1.5),
            ("250", 250, 1.5),
            ("500", 500, 1.5),
            ("1k", 1000, 1.5),
            ("2k", 2000, 1.5),
            ("4k", 4000, 1.5),
            ("8k", 8000, 1.5),
            ("16k", 16000, 1.5)
        };

        public const int BandCount = 10;

        public float[] BandGains { get; } = new float[BandCount]; // -12..+12 dB, по умолчанию 0

        public EqSampleProvider(ISampleProvider source)
        {
            _source = source;
            _channels = source.WaveFormat.Channels;
            _filters = new BiQuadFilter[_channels * BandCount];

            for (int ch = 0; ch < _channels; ch++)
            {
                for (int b = 0; b < BandCount; b++)
                {
                    _filters[ch * BandCount + b] = BiQuadFilter.PeakingEQ(
                        source.WaveFormat.SampleRate,
                        (float)Bands[b].Frequency,
                        (float)Bands[b].Bandwidth,
                        0f);
                }
            }
        }

        public void UpdateBand(int bandIndex, float gainDb)
        {
            if (bandIndex < 0 || bandIndex >= BandCount) return;
            BandGains[bandIndex] = gainDb;

            for (int ch = 0; ch < _channels; ch++)
            {
                _filters[ch * BandCount + bandIndex] = BiQuadFilter.PeakingEQ(
                    _source.WaveFormat.SampleRate,
                    (float)Bands[bandIndex].Frequency,
                    (float)Bands[bandIndex].Bandwidth,
                    gainDb);
            }
        }

        public void ApplyGains(float[] gains)
        {
            for (int i = 0; i < BandCount && i < gains.Length; i++)
                UpdateBand(i, gains[i]);
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);
            if (IsNeutral()) return samplesRead;

            for (int i = 0; i < samplesRead; i++)
            {
                int channel = i % _channels;
                float sample = buffer[offset + i];

                for (int b = 0; b < BandCount; b++)
                {
                    sample = _filters[channel * BandCount + b].Transform(sample);
                }

                buffer[offset + i] = sample;
            }
            return samplesRead;
        }

        private bool IsNeutral()
        {
            for (int i = 0; i < BandCount; i++)
                if (Math.Abs(BandGains[i]) > 0.01f) return false;
            return true;
        }
    }
}
