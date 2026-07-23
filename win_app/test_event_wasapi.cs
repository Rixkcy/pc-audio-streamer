using System;
using System.Diagnostics;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace EventWasapiTest
{
    class Program
    {
        static void Main()
        {
            try
            {
                var enumerator = new MMDeviceEnumerator();
                var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                MMDevice cableDev = null;
                foreach (var dev in devices)
                {
                    if (dev.FriendlyName.Contains("CABLE"))
                    {
                        cableDev = dev;
                        break;
                    }
                }

                if (cableDev == null)
                {
                    Console.WriteLine("CABLE Input not found");
                    return;
                }

                Console.WriteLine($"Testing Event-Driven WASAPI on: {cableDev.FriendlyName}");
                var capture = new WasapiLoopbackCapture(cableDev);

                Stopwatch sw = Stopwatch.StartNew();
                long lastMs = 0;
                int count = 0;

                capture.DataAvailable += (s, e) =>
                {
                    count++;
                    long currentMs = sw.ElapsedMilliseconds;
                    long delta = currentMs - lastMs;
                    lastMs = currentMs;

                    if (count <= 30)
                    {
                        Console.WriteLine($"[Event Callback #{count}] Delta: {delta}ms | Bytes: {e.BytesRecorded}");
                    }
                };

                capture.StartRecording();
                Thread.Sleep(3000);
                capture.StopRecording();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex}");
            }
        }
    }
}
