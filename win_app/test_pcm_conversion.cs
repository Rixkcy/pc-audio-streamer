using System;
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ConversionTest
{
    class Program
    {
        static void Main()
        {
            var enumerator = new MMDeviceEnumerator();
            var dev = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            Console.WriteLine($"Capturing 2 seconds from: {dev.FriendlyName}");
            Console.WriteLine($"Volume: {dev.AudioEndpointVolume.MasterVolumeLevelScalar:P0} | Muted: {dev.AudioEndpointVolume.Mute}");

            var capture = new WasapiLoopbackCapture(dev);
            WaveFileWriter writer = null;

            capture.DataAvailable += (s, e) =>
            {
                if (writer == null)
                {
                    writer = new WaveFileWriter("test_out.wav", new WaveFormat(48000, 16, 2));
                }

                // Convert float to pcm16
                byte[] pcm16 = ConvertFloatToPcm16Aligned(e.Buffer, e.BytesRecorded);

                // Print first 5 samples as hex & short values
                Console.WriteLine($"\nReceived WASAPI Bytes: {e.BytesRecorded} -> PCM16 Bytes: {pcm16.Length}");
                for (int i = 0; i < Math.Min(10, pcm16.Length / 2); i++)
                {
                    short s16 = (short)(pcm16[i * 2] | (pcm16[i * 2 + 1] << 8));
                    float f32 = BitConverter.ToSingle(e.Buffer, i * 4);
                    Console.WriteLine($"  Sample [{i}]: Float={f32:F4} -> PCM16={s16}");
                }

                writer.Write(pcm16, 0, pcm16.Length);
            };

            capture.StartRecording();
            System.Threading.Thread.Sleep(2000);
            capture.StopRecording();
            writer?.Dispose();
            Console.WriteLine("\nSaved to test_out.wav successfully!");
        }

        private static byte[] ConvertFloatToPcm16Aligned(byte[] inputBuffer, int length)
        {
            int alignedLength = (length / 8) * 8;
            int sampleCount = alignedLength / 4;
            byte[] outputBuffer = new byte[sampleCount * 2];

            for (int i = 0; i < sampleCount; i++)
            {
                float floatSample = BitConverter.ToSingle(inputBuffer, i * 4);
                if (floatSample > 1.0f) floatSample = 1.0f;
                if (floatSample < -1.0f) floatSample = -1.0f;

                short shortSample = (short)(floatSample * 32767.0f);
                outputBuffer[i * 2] = (byte)(shortSample & 0xFF);
                outputBuffer[i * 2 + 1] = (byte)((shortSample >> 8) & 0xFF);
            }
            return outputBuffer;
        }
    }
}
