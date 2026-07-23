using System;
using NAudio.CoreAudioApi;

namespace DeviceTest
{
    class Program
    {
        static void Main()
        {
            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

            Console.WriteLine("=== SYSTEM AUDIO RENDER ENDPOINTS ===");
            foreach (var dev in devices)
            {
                Console.WriteLine($"Name: {dev.FriendlyName}");
                Console.WriteLine($"  ID: {dev.ID}");
                try
                {
                    var client = dev.AudioClient;
                    Console.WriteLine($"  DefaultPeriod: {client.DefaultDevicePeriod / 10000.0}ms");
                    Console.WriteLine($"  MinimumPeriod: {client.MinimumDevicePeriod / 10000.0}ms");
                }
                catch { }
                Console.WriteLine();
            }
        }
    }
}
