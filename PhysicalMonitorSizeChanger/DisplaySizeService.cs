using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PhysicalMonitorSizeChanger;

// 1. 현재 모니터의 물리적 크기 정보 DTO
public sealed record DisplayPhysicalInfo(
    bool Found,
    string InstanceId,
    string HardwareId,
    int WidthCm,
    int HeightCm,
    double DiagonalInches,
    int WidthPx,
    int HeightPx,
    string RatioText,
    bool IsOverridden)
{
    // 드롭다운(ComboBox)에 표시될 이름
    public string DisplayLabel =>
        $"{HardwareId} ({DiagonalInches:0.0}\" / {WidthCm}×{HeightCm}cm / {WidthPx}×{HeightPx})" +
        (IsOverridden ? " [수정됨]" : "");
}

// 2. 목표 인치 -> 가로/세로 cm 계산 결과 DTO
public sealed record CalculatedDisplaySize(
    int WidthCm,
    int HeightCm,
    double CalculatedInches,
    string RatioText);

// 3. EDID 조회·계산·패치·복원 서비스
public static class DisplaySizeService
{
    private const string EnumDisplayRoot = @"SYSTEM\CurrentControlSet\Enum\DISPLAY";
    private const string PreferredVirtualId = "SDD5BC2";

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private static string GetBackupFilePath(string hardwareId) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PhysicalMonitorSizeChanger", $"edid_backup_{hardwareId}.bin");

    /// <summary>레지스트리에서 EDID가 존재하는 모니터 목록을 조회합니다 (SDD5BC2 우선 정렬).</summary>
    public static List<DisplayPhysicalInfo> GetAvailableMonitors()
    {
        var list = new List<DisplayPhysicalInfo>();
        try
        {
            using var displayRoot = Registry.LocalMachine.OpenSubKey(EnumDisplayRoot);
            if (displayRoot == null) return list;

            foreach (string hwId in displayRoot.GetSubKeyNames())
            {
                using var hwKey = displayRoot.OpenSubKey(hwId);
                if (hwKey == null) continue;

                foreach (string subName in hwKey.GetSubKeyNames())
                {
                    string instanceId = $@"DISPLAY\{hwId}\{subName}";
                    var info = GetMonitorInfo(instanceId, hwId);
                    if (info.Found)
                        list.Add(info);
                }
            }
        }
        catch { }

        return list
            .OrderByDescending(m => m.HardwareId.Equals(PreferredVirtualId, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(m => m.IsOverridden)
            .ToList();
    }

    /// <summary>특정 모니터 InstanceId의 현재 EDID 정보를 읽습니다.</summary>
    public static DisplayPhysicalInfo GetMonitorInfo(string instanceId, string? hardwareId = null)
    {
        string hwId = hardwareId ?? instanceId.Split('\\').ElementAtOrDefault(1) ?? "UNKNOWN";
        int wPx = Math.Max(1, GetSystemMetrics(0)); // SM_CXSCREEN
        int hPx = Math.Max(1, GetSystemMetrics(1)); // SM_CYSCREEN

        string devParamPath = $@"SYSTEM\CurrentControlSet\Enum\{instanceId}\Device Parameters";
        using var devKey = Registry.LocalMachine.OpenSubKey(devParamPath);
        if (devKey == null)
            return new(false, instanceId, hwId, 0, 0, 0, wPx, hPx, FormatRatio(wPx, hPx), false);

        byte[]? edid = null;
        bool isOverridden = false;

        using (var overrideKey = devKey.OpenSubKey("EDID_OVERRIDE"))
        {
            if (overrideKey?.GetValue("0") is byte[] ov && ov.Length >= 128)
            {
                edid = ov;
                isOverridden = true;
            }
        }

        if (edid == null && devKey.GetValue("EDID") is byte[] raw && raw.Length >= 128)
            edid = raw;

        if (edid == null || edid.Length < 23)
            return new(false, instanceId, hwId, 0, 0, 0, wPx, hPx, FormatRatio(wPx, hPx), isOverridden);

        // EDID DTD1 블록(바이트 54..71)에서 모니터 고유 해상도 추출
        if (edid.Length >= 72 && (edid[54] != 0 || edid[55] != 0))
        {
            int dtdW = edid[56] | ((edid[58] >> 4) << 8);
            int dtdH = edid[59] | ((edid[61] >> 4) << 8);
            if (dtdW >= 640 && dtdH >= 480)
            {
                wPx = dtdW;
                hPx = dtdH;
            }
        }

        int wCm = edid[21];
        int hCm = edid[22];
        double diagInches = Math.Round(Math.Sqrt(wCm * wCm + hCm * hCm) / 2.54, 1);

        return new DisplayPhysicalInfo(
            Found: true,
            InstanceId: instanceId,
            HardwareId: hwId,
            WidthCm: wCm,
            HeightCm: hCm,
            DiagonalInches: diagInches,
            WidthPx: wPx,
            HeightPx: hPx,
            RatioText: $"{FormatRatio(wPx, hPx)} ({wPx}×{hPx})",
            IsOverridden: isOverridden);
    }

    /// <summary>목표 인치와 해상도 비율로 가로/세로 cm를 역산합니다.</summary>
    public static CalculatedDisplaySize CalculateTargetSize(double targetInches, int widthPx, int heightPx)
    {
        double ratio = (double)Math.Max(1, widthPx) / Math.Max(1, heightPx);
        double diagonalCm = targetInches * 2.54;

        int heightCm = Math.Clamp((int)Math.Round(Math.Sqrt((diagonalCm * diagonalCm) / (ratio * ratio + 1.0))), 1, 255);
        int widthCm = Math.Clamp((int)Math.Round(ratio * heightCm), 1, 255);

        double calcInches = Math.Round(Math.Sqrt(widthCm * widthCm + heightCm * heightCm) / 2.54, 1);
        string ratioText = $"{FormatRatio(widthCm, heightCm)} ({widthCm}cm × {heightCm}cm)";

        return new CalculatedDisplaySize(widthCm, heightCm, calcInches, ratioText);
    }

    public static bool ApplyPhysicalSize(DisplayPhysicalInfo monitor, double targetInches, out string message)
    {
        var calc = CalculateTargetSize(targetInches, monitor.WidthPx, monitor.HeightPx);
        string backupPath = GetBackupFilePath(monitor.HardwareId);
        string args = $"--apply-edid \"{monitor.InstanceId}\" {calc.WidthCm} {calc.HeightCm} \"{backupPath}\"";

        if (RunElevatedSelf(args, out int exitCode) && exitCode == 0)
        {
            message = $"적용 완료: {calc.CalculatedInches:0.0}인치 ({calc.WidthCm}cm × {calc.HeightCm}cm)";
            return true;
        }
        message = "적용 실패 (관리자 권한이 취소되었거나 레지스트리 쓰기에 실패했습니다).";
        return false;
    }

    public static bool RestoreInitialSize(DisplayPhysicalInfo monitor, out string message)
    {
        string backupPath = GetBackupFilePath(monitor.HardwareId);
        string args = $"--restore-edid \"{monitor.InstanceId}\" \"{backupPath}\"";

        if (RunElevatedSelf(args, out int exitCode) && exitCode == 0)
        {
            var updated = GetMonitorInfo(monitor.InstanceId, monitor.HardwareId);
            message = $"복원 완료: {updated.DiagonalInches:0.0}인치 ({updated.WidthCm}cm × {updated.HeightCm}cm)";
            return true;
        }
        message = "복원 실패 (관리자 권한이 취소되었거나 원본 백업을 찾을 수 없습니다).";
        return false;
    }

    public static bool TryHandleElevatedCommand(string[] args)
    {
        if (args.Length == 0) return false;
        string cmd = args[0];
        if (cmd != "--apply-edid" && cmd != "--restore-edid")
            return false;

        try
        {
            if (cmd == "--apply-edid" && args.Length >= 5 &&
                int.TryParse(args[2], out int wCm) && int.TryParse(args[3], out int hCm))
            {
                Environment.ExitCode = ExecuteApplyEdid(args[1], wCm, hCm, args[4]) ? 0 : 1;
            }
            else if (cmd == "--restore-edid" && args.Length >= 3)
            {
                Environment.ExitCode = ExecuteRestoreEdid(args[1], args[2]) ? 0 : 1;
            }
            else
            {
                Environment.ExitCode = 2;
            }
        }
        catch
        {
            Environment.ExitCode = 1;
        }
        return true;
    }

    private static bool ExecuteApplyEdid(string instanceId, int widthCm, int heightCm, string backupPath)
    {
        string devParamPath = $@"SYSTEM\CurrentControlSet\Enum\{instanceId}\Device Parameters";
        using var devKey = Registry.LocalMachine.OpenSubKey(devParamPath, writable: true);
        if (devKey?.GetValue("EDID") is not byte[] currentEdid || currentEdid.Length < 128)
            return false;

        byte[] baseEdid;
        if (devKey.GetValue("EDID_BACKUP") is byte[] regBackup && regBackup.Length >= 128)
        {
            baseEdid = (byte[])regBackup.Clone();
        }
        else if (File.Exists(backupPath))
        {
            baseEdid = File.ReadAllBytes(backupPath);
            devKey.SetValue("EDID_BACKUP", baseEdid, RegistryValueKind.Binary);
        }
        else
        {
            baseEdid = (byte[])currentEdid.Clone();
            devKey.SetValue("EDID_BACKUP", baseEdid, RegistryValueKind.Binary);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                File.WriteAllBytes(backupPath, baseEdid);
            }
            catch { }
        }

        byte[] patched = (byte[])baseEdid.Clone();
        PatchEdidDimensions(patched, widthCm, heightCm);

        devKey.SetValue("EDID", patched, RegistryValueKind.Binary);
        using (var ovKey = devKey.CreateSubKey("EDID_OVERRIDE"))
        {
            byte[] block0 = new byte[128];
            Array.Copy(patched, 0, block0, 0, 128);
            ovKey.SetValue("0", block0, RegistryValueKind.Binary);
        }

        RestartDisplayAndTouchKeyboard(instanceId);
        return true;
    }

    private static bool ExecuteRestoreEdid(string instanceId, string backupPath)
    {
        string devParamPath = $@"SYSTEM\CurrentControlSet\Enum\{instanceId}\Device Parameters";
        using var devKey = Registry.LocalMachine.OpenSubKey(devParamPath, writable: true);
        if (devKey == null) return false;

        byte[]? original = null;
        if (devKey.GetValue("EDID_BACKUP") is byte[] regBackup && regBackup.Length >= 128)
            original = (byte[])regBackup.Clone();
        else if (File.Exists(backupPath))
            original = File.ReadAllBytes(backupPath);

        if (original is { Length: >= 128 })
            devKey.SetValue("EDID", original, RegistryValueKind.Binary);

        devKey.DeleteSubKeyTree("EDID_OVERRIDE", throwOnMissingSubKey: false);
        RestartDisplayAndTouchKeyboard(instanceId);
        return true;
    }

    private static void PatchEdidDimensions(byte[] edid, int widthCm, int heightCm)
    {
        byte wCm = (byte)Math.Clamp(widthCm, 1, 255);
        byte hCm = (byte)Math.Clamp(heightCm, 1, 255);
        edid[21] = wCm;
        edid[22] = hCm;

        if (edid.Length >= 72 && (edid[54] != 0 || edid[55] != 0))
        {
            int widthMm = Math.Clamp(wCm * 10, 1, 4095);
            int heightMm = Math.Clamp(hCm * 10, 1, 4095);
            edid[66] = (byte)(widthMm & 0xFF);
            edid[67] = (byte)(heightMm & 0xFF);
            edid[68] = (byte)((((widthMm >> 8) & 0x0F) << 4) | ((heightMm >> 8) & 0x0F));
        }

        edid[127] = 0;
        int sum = 0;
        for (int i = 0; i < 127; i++) sum += edid[i];
        edid[127] = (byte)((256 - (sum % 256)) % 256);
    }

    private static void RestartDisplayAndTouchKeyboard(string instanceId)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("pnputil.exe", $"/restart-device \"{instanceId}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            p?.WaitForExit(10000);
        }
        catch { }

        try
        {
            foreach (var proc in Process.GetProcessesByName("TextInputHost"))
            {
                try { proc.Kill(); }
                catch { }
                finally { proc.Dispose(); }
            }
        }
        catch { }
    }

    private static bool RunElevatedSelf(string arguments, out int exitCode)
    {
        exitCode = -1;
        if (string.IsNullOrEmpty(Environment.ProcessPath)) return false;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Environment.ProcessPath, arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (p == null) return false;
            p.WaitForExit(15000);
            exitCode = p.ExitCode;
            return true;
        }
        catch (Win32Exception) { return false; }
        catch { return false; }
    }

    private static string FormatRatio(int w, int h)
    {
        if (w <= 0 || h <= 0) return "-";
        int a = w, b = h;
        while (b != 0) { int t = b; b = a % b; a = t; }
        int g = Math.Max(1, a);
        int rw = w / g, rh = h / g;
        return (rw <= 32 && rh <= 32) ? $"{rw}:{rh}" : $"{(double)w / h:0.00}:1";
    }
}