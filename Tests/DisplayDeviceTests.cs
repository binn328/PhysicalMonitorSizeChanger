using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Xunit;
using Xunit.Abstractions;

namespace PhysicalMonitorSizeChanger.Tests;

public class DisplayDeviceTest(ITestOutputHelper output)
{
    [Fact]
    public void PrintAllAdaptersAndMonitors()
    {
        var adapter = new DISPLAY_DEVICEW();
        adapter.cb = (uint)Marshal.SizeOf<DISPLAY_DEVICEW>();

        for (uint i = 0; PInvoke.EnumDisplayDevices(null, i, ref adapter, 0); i++)
        {
            // 💡 중요: CsWin32의 __char_32에서 첫 '\0'(문자열 끝)까지만 깔끔하게 잘라내기
            string adapterName = adapter.DeviceName.ToString().Split('\0')[0];
            string adapterDesc = adapter.DeviceString.ToString().Split('\0')[0];
            bool isActive = adapter.StateFlags.HasFlag(DISPLAY_DEVICE_STATE_FLAGS.DISPLAY_DEVICE_ACTIVE);

            output.WriteLine($"[포트 {i}] {adapterName} | {adapterDesc} | Active={isActive} | Flags={adapter.StateFlags}");

            // 해당 포트에 연결된 모니터 조회 (2차 호출)
            var monitor = new DISPLAY_DEVICEW();
            monitor.cb = (uint)Marshal.SizeOf<DISPLAY_DEVICEW>();

            for (uint j = 0; PInvoke.EnumDisplayDevices(adapterName, j, ref monitor, PInvoke.EDD_GET_DEVICE_INTERFACE_NAME); j++)
            {
                string monName = monitor.DeviceString.ToString().Split('\0')[0];
                string monId = monitor.DeviceID.ToString().Split('\0')[0];
                bool monActive = monitor.StateFlags.HasFlag(DISPLAY_DEVICE_STATE_FLAGS.DISPLAY_DEVICE_ACTIVE);

                output.WriteLine($"   👉 [모니터 {j}] 이름={monName} | Active={monActive}");
                output.WriteLine($"   👉 [모니터 {j}] ID={monId}");
            }
        }
    }
}