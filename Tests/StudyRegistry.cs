using Microsoft.Win32;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Xunit.Abstractions;

namespace Tests;

public class StudyRegistry(ITestOutputHelper output)
{
    [Fact]
    public void DisplayFindTest()
    {
        // 레지스트리 경로 상수
        const string EnumDisplayRoot = @"SYSTEM\CurrentControlSet\Enum\DISPLAY";

        // 어댑터 조회 준비
        var adapter = new DISPLAY_DEVICEW
        {
            cb = (uint)Marshal.SizeOf<DISPLAY_DEVICEW>()
        };

        for (uint i = 0; PInvoke.EnumDisplayDevices(null, i, ref adapter, 1); i++)
        {
            // 비활성 어댑터면 패스
            if (!adapter.StateFlags.HasFlag(DISPLAY_DEVICE_STATE_FLAGS.DISPLAY_DEVICE_ACTIVE)) continue;

            string adapterName = adapter.DeviceName.ToString();

            var monitor = new DISPLAY_DEVICEW();
            monitor.cb = (uint)Marshal.SizeOf<DISPLAY_DEVICEW>();

            for (uint j = 0; PInvoke.EnumDisplayDevices(adapterName, j, ref monitor, 1); j++)
            {
                if (!monitor.StateFlags.HasFlag(DISPLAY_DEVICE_STATE_FLAGS.DISPLAY_DEVICE_ACTIVE)) continue;

                string monitorName = monitor.DeviceName.ToString();
                string monitorId = monitor.DeviceID.ToString();
                string monitorString = monitor.DeviceString.ToString();
                string monitorKey = monitor.DeviceKey.ToString();

                output.WriteLine($"모니터 이름: {monitorName}");
                output.WriteLine($"모니터 ID: {monitorId}");
                output.WriteLine($"모니터 문자열: {monitorString}");
                output.WriteLine($"모니터 키: {monitorKey}");
                string[] parts = monitorId.Split("#");
                string monitorCode = parts[1];
                string monitorRegestry = parts[2];
                output.WriteLine($"모니터 코드: {parts[1]}");
                output.WriteLine($"모니터 레지스트리: {parts[2]}");

                string regestryAddress = $@"{EnumDisplayRoot}\{parts[1]}\{parts[2]}";
                output.WriteLine($"레지스트리 주소: {regestryAddress}");

                using var hwKey = Registry.LocalMachine.OpenSubKey(regestryAddress);

                output.WriteLine(hwKey.ToString());
            }
        }
    }
}