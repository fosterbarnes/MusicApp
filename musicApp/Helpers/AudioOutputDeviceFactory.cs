using System;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace musicApp.Helpers
{
    public static class AudioOutputDeviceFactory
    {
        public static IWavePlayer Create(AudioOutputBackend backend)
        {
            try
            {
                return backend switch
                {
                    AudioOutputBackend.WasapiExclusive => new WasapiOut(AudioClientShareMode.Exclusive, 50),
                    AudioOutputBackend.DirectSound => new DirectSoundOut(100),
                    AudioOutputBackend.WaveOut => new WaveOutEvent() { DesiredLatency = 100 },
                    _ => new WasapiOut(AudioClientShareMode.Shared, 50)
                };
            }
            catch (Exception)
            {
                // Exclusive can fail (device/format); shared WASAPI is the fallback.
                if (backend == AudioOutputBackend.WasapiExclusive)
                    return new WasapiOut(AudioClientShareMode.Shared, 50);
                throw;
            }
        }
    }
}
