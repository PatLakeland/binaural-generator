﻿using NAudio.Wave;

namespace SharedLibrary.AudioProviders
{
    public abstract class SampleProvider : ISampleProvider
    {
        readonly protected WaveFormat waveFormat;

        public WaveFormat WaveFormat
        {
            get { return waveFormat; }
        }

        public float Gain { get; set; } = 1.0f;

        public SampleProvider()
        {
            waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate: 44100, channels: 2);
        }

        public abstract int Read(float[] buffer, int offset, int count);
    }
}
