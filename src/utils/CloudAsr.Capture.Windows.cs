#if WINDOWS
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LiveCaptionsTranslator.utils
{
    // Audio capture of CloudAsr on Windows: the computer's output (WASAPI loopback) or the default microphone.
    public static partial class CloudAsr
    {
        private static IWaveIn? capture;

        private static void BeginCapture(bool useMicrophone)
        {
            capture = useMicrophone ? new WasapiCapture() : new WasapiLoopbackCapture();
            var format = capture.WaveFormat;
            bool isFloat = format.BitsPerSample == 32 && format.Encoding != WaveFormatEncoding.Pcm;
            capture.DataAvailable += (s, e) =>
                OnAudio(e.Buffer, e.BytesRecorded, format.SampleRate, format.Channels, format.BitsPerSample, isFloat);
            capture.StartRecording();
        }

        private static void EndCapture()
        {
            try
            {
                capture?.StopRecording();
                capture?.Dispose();
            }
            catch (Exception)
            {
            }
            capture = null;
        }
    }
}
#endif
