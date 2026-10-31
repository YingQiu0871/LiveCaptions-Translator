using System.Globalization;
using System.IO;
using System.Speech.Synthesis;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LiveCaptionsTranslator.utils
{
    // Reads summaries (and optionally translations) aloud through the default audio output device,
    // e.g. the connected Bluetooth earphones. The queue and the rules for what is read are in Speaker.Queue.cs.
    public static partial class Speaker
    {
        private static SpeechSynthesizer? synthesizer;

        public static void StopAll()
        {
            ClearQueue();
            stopRequested = true;
            try
            {
                synthesizer?.SpeakAsyncCancelAll();
            }
            catch (Exception)
            {
                // Best effort: the synthesizer may be idle or disposed.
            }
        }

        public static List<string> GetVoices()
        {
            try
            {
                using var synth = new SpeechSynthesizer();
                return synth.GetInstalledVoices()
                    .Where(v => v.Enabled)
                    .Select(v => v.VoiceInfo.Name)
                    .ToList();
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }

        private static void Worker()
        {
            try
            {
                synthesizer = new SpeechSynthesizer();
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] 语音播报不可用。", ex.Message, SnackbarType.Error,
                    timeout: 5, closeButton: true);
                return;
            }

            while (true)
            {
                signal.WaitOne();
                while (TryDequeue(out string text))
                {
                    stopRequested = false;
                    try
                    {
                        ApplySettings(synthesizer);
                        string deviceId = Lecture?.SpeechDeviceId ?? string.Empty;
                        using var device = string.IsNullOrEmpty(deviceId) ? null : AudioDevices.Get(deviceId);

                        // Re-checked every time: the default device changes when earphones connect or disconnect.
                        speakingOnDefaultOutput = device == null ||
                                                  device.ID == AudioDevices.DefaultId(DataFlow.Render);
                        if (device == null)
                            SpeakToDefaultDevice(synthesizer, text);
                        else
                            SpeakToDevice(synthesizer, text, device);
                    }
                    catch (Exception ex)
                    {
                        SnackbarHost.Show("[ERROR] 语音播报失败。", ex.Message, SnackbarType.Error,
                            timeout: 3, closeButton: true);
                    }
                    finally
                    {
                        if (speakingOnDefaultOutput)
                            suppressUntil = DateTime.Now + SUPPRESS_TAIL;
                        speakingOnDefaultOutput = false;
                    }
                }
            }
        }

        private static void SpeakToDefaultDevice(SpeechSynthesizer synth, string text)
        {
            synth.SetOutputToDefaultAudioDevice();
            var prompt = synth.SpeakAsync(text);
            while (!prompt.IsCompleted)
                Thread.Sleep(50);
        }

        // Plays on a chosen device (e.g. the earphones) while the Windows default output stays elsewhere.
        private static void SpeakToDevice(SpeechSynthesizer synth, string text, MMDevice device)
        {
            // 48 kHz stereo matches the shared-mode mix format of most devices.
            using var stream = new MemoryStream();
            synth.SetOutputToAudioStream(stream,
                new System.Speech.AudioFormat.SpeechAudioFormatInfo(48000,
                    System.Speech.AudioFormat.AudioBitsPerSample.Sixteen, System.Speech.AudioFormat.AudioChannel.Stereo));
            try
            {
                synth.Speak(text);
            }
            finally
            {
                synth.SetOutputToNull();
            }
            if (stopRequested)
                return;

            stream.Position = 0;
            using var reader = new RawSourceWaveStream(stream, new WaveFormat(48000, 16, 2));
            using var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
            output.Init(reader);
            output.Play();
            while (output.PlaybackState == PlaybackState.Playing && !stopRequested)
                Thread.Sleep(50);
            output.Stop();
        }

        private static void ApplySettings(SpeechSynthesizer synth)
        {
            var lecture = Lecture;
            if (lecture == null)
                return;

            synth.Rate = lecture.SpeechRate;
            synth.Volume = lecture.SpeechVolume;

            string wanted = lecture.VoiceName;
            if (!string.IsNullOrEmpty(wanted))
            {
                if (synth.Voice.Name != wanted)
                {
                    try
                    {
                        synth.SelectVoice(wanted);
                        return;
                    }
                    catch (Exception)
                    {
                        // The configured voice is unavailable; fall back to choosing one by language.
                    }
                }
                else
                    return;
            }

            // No voice chosen: pick one that speaks the target language (the lecture's own in 原语言模式).
            try
            {
                var culture = new CultureInfo(SpeechLanguage());
                if (!synth.Voice.Culture.Name.Equals(culture.Name, StringComparison.OrdinalIgnoreCase))
                    synth.SelectVoiceByHints(VoiceGender.NotSet, VoiceAge.NotSet, 0, culture);
            }
            catch (Exception)
            {
                // Best effort: keep the current voice if none matches the language.
            }
        }
    }
}
