using Microsoft.Win32;
using PhysicalMonitorSizeChanger;
using Xunit.Abstractions;

namespace Tests;

public class DisplayServiceTests(ITestOutputHelper output)
{
    private const string EnumDisplayRoot = @"SYSTEM\CurrentControlSet\Enum\DISPLAY";
    [Fact]
    public void GetDisplayRegistryTest()
    {
        var registryList = DisplayService.GetDisplayInstanceIds();
        Assert.Single(registryList);
        output.WriteLine($"DISPLAY: {registryList[0]}");

        using var displayKey = Registry.LocalMachine.OpenSubKey(@$"{EnumDisplayRoot}\{registryList[0]}");

        string friendlyName = displayKey.GetValue("FriendlyName").ToString();
        string[] nameParts = friendlyName.Split(";");
        output.WriteLine(nameParts.ToString());

        /* 이름이 있으면 3개, 아니면 2개임
         * 있으면 [2] 취해서 () 없애기
         * 없으면 [1] 취하기 */
        //"Generic Monitor (%1)"
        //"(Desktop)"
    }

    [Fact]
    public void GetDisplayInfoTest()
    {
        var registryList = DisplayService.GetDisplayInstanceIds();
        var displayInfo = DisplayService.GetDisplayInfo(registryList[0]);

        Assert.NotNull(displayInfo);

        output.WriteLine(displayInfo.ToString());
    }
}
