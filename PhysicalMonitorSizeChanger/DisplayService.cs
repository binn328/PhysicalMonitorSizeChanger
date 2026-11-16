using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;

namespace PhysicalMonitorSizeChanger;

/// <summary>
/// 디스플레이 정보 레코드
/// </summary>
/// <param name="Name"></param>
/// <param name="InstanceId"></param>
/// <param name="HardwareId"></param>
/// <param name="RegistryAddress"></param>
/// <param name="Inches"></param>
/// <param name="WidthCm"></param>
/// <param name="HeightCm"></param>
/// <param name="WidthPx"></param>
/// <param name="HeightPx"></param>
/// <param name="IsOverriden"></param>
public sealed record DisplayInfo(
    string RegistryAddress,
    string InstanceId,
    string Name,
    string HardwareId,
    double Inches,
    int WidthCm,
    int HeightCm,
    int WidthPx,
    int HeightPx,
    bool IsOverridden
)
{
    public string DisplayLabel =>
        $"{Name} [{HardwareId}] ({Inches:0.0}\" / {WidthCm}×{HeightCm}cm)" +
        (IsOverridden ? " [수정됨]" : "");
}

/// <summary>
/// 목표 인치 수를 만들기 위한 가로 세로 cm 레코드
/// </summary>
/// <param name="WidthCm"></param>
/// <param name="HeightCm"></param>
/// <param name="Inches"></param>
public sealed record TargetSize(
    int WidthCm,
    int HeightCm,
    double Inches
);

public class DisplayService
{
    // 레지스트리 경로 상수
    private const string EnumDisplayRoot = @"SYSTEM\CurrentControlSet\Enum\DISPLAY";

    /// <summary>
    /// 활성화된 어댑터에 연결된 디스플레이 인스턴스 ID 리스트로 반환한다.
    /// </summary>
    /// <returns>디스플레이 인스턴스 ID 리스트</returns>
    public static List<string> GetDisplayInstanceIds()
    {
        List<string> list = new();

        // 디스플레이 어댑터 조회 준비
        var adapter = new DISPLAY_DEVICEW
        {
            cb = (uint)Marshal.SizeOf<DISPLAY_DEVICEW>()
        };

        // 모든 디스플레이 어댑터 조회
        for (uint i = 0; PInvoke.EnumDisplayDevices(null, i, ref adapter, 1); i++)
        {
            // 비활성 어댑터면 넘김
            if (!adapter.StateFlags.HasFlag(DISPLAY_DEVICE_STATE_FLAGS.DISPLAY_DEVICE_ACTIVE)) continue;

            string adapterName = adapter.DeviceName.ToString();

            var monitor = new DISPLAY_DEVICEW
            {
                cb = (uint)Marshal.SizeOf<DISPLAY_DEVICEW>()
            };

            // 활성 어댑터에 연결된 모니터 조회
            for (uint j = 0; PInvoke.EnumDisplayDevices(adapterName, j, ref monitor, 1); j++)
            {
                // 비활성 모니터면 넘김
                if (!monitor.StateFlags.HasFlag(DISPLAY_DEVICE_STATE_FLAGS.DISPLAY_DEVICE_ACTIVE)) continue;

                string monitorId = monitor.DeviceID.ToString();

                string[] parts = monitorId.Split("#");

                // 길이 3 미만이면 필요한 정보 없음. 넘김.
                if (parts.Length < 3) continue;

                string registryAddress = $@"{parts[1]}\{parts[2]}";
                list.Add(registryAddress);
            }
        }
        return list;
    }

    /// <summary>
    /// 디스플레이 인스턴스 ID를 받아 EDID를 조회해 디스플레이 정보 레코드를 반환한다.
    /// </summary>
    /// <param name="displayInstanceId"></param>
    /// <returns>디스플레이 정보 레코드</returns>
    public static DisplayInfo? GetDisplayInfo(string displayInstanceId)
    {
        /* 1. RegistryAddress 만들기 */
        string registryAddress = $@"{EnumDisplayRoot}\{displayInstanceId}";
        try
        {
            /* 디스플레이 레지스트리 키를 가져옴 */
            using var displayKey = Registry.LocalMachine.OpenSubKey(registryAddress);
            if (displayKey is null) return null;

            /* 2. Name 가져오기 */
            /* 디스플레이 이름을 가져온다 */
            string friendlyName = displayKey.GetValue("FriendlyName")?.ToString() ?? "";

            string[] partsName = friendlyName.Split(";");
            string name = "Generic Monitor";

            /* 자체적인 이름이 있는 경우 3개, 아니면 2개이다. */
            if (partsName.Length == 2)
            {
                // ex) Generic Monitor
                name = partsName[1];
            }
            else if (partsName.Length > 2)
            {
                // ex) (Desktop)
                name = partsName[2].Replace("(", "").Replace(")", "");
            }

            /* 3. HardwareId 가져오기 */
            // 여기 오는 instanceId는 SDSDSD\4&a2039... 구조인게... 보장되나?
            string[] partsIdsplayInstanceId = displayInstanceId.Split(@"\");
            if (partsIdsplayInstanceId.Length < 2) return null;

            string hardwareId = partsIdsplayInstanceId[0];

            /* 4. EDID 정보 분석 */
            using var edidKey = displayKey.OpenSubKey("Device Parameters");
            if (edidKey is null) return null;

            byte[]? edid = null;
            bool isOverridden = false;

            /* 4-1. 덮어쓴 정보가 있는지 확인 */
            {
                using var overrideKey = edidKey.OpenSubKey("EDID_OVERRIDE");
                if (overrideKey?.GetValue("0") is byte[] overriddenEdid && overriddenEdid.Length >= 128)
                {
                    edid = overriddenEdid;
                    isOverridden = true;

                }
            }
            /* 4-2. 덮어쓴 정보가 없으면 원본을 가져옴 */
            if (edid is null && edidKey.GetValue("EDID") is byte[] rawEdid && rawEdid.Length >= 128)
            {
                edid = rawEdid;
            }

            /* 4-3. EDID 정보가 없거나 byte 길이가 23 미만이면 실패 */
            if (edid is null || edid.Length < 23) return null;


            /* 5. EDID DTD1(바이트 54..71) 블럭에서 모니터 고유 해상도 추출 */
            int widthPx = 0;
            int heightPx = 0;
            if (edid.Length >= 72 && (edid[54] != 0 || edid[55] != 0))
            {
                int dtdW = edid[56] | ((edid[58] >> 4) << 8);
                int dtdH = edid[59] | ((edid[61] >> 4) << 8);
                if (dtdW >= 640 && dtdH >= 480)
                {
                    widthPx = dtdW;
                    heightPx = dtdH;
                }
            }

            /* 6. 모니터 cm 가로 세로 길이 추출 */
            int widthCm = edid[21];
            int heightCm = edid[22];
            
            /* 7. 대각선 인치(1" = 2.54cm) 계산 => sqrt(w^2 + h^2) / 2.54 */
            double inches = Math.Round(Math.Sqrt(widthCm * widthCm + heightCm * heightCm) / 2.54, 1);

            return new DisplayInfo(
                registryAddress,
                displayInstanceId,
                name,
                hardwareId,
                inches,
                widthCm,
                heightCm,
                widthPx,
                heightPx,
                isOverridden);
        }
        catch (Exception e)
        {
            // 찰나의 시간에 디스플레이를 분리한 경우,
            // 관리자 권한이 없어 읽어오지 못한 경우 등...
            Debug.WriteLine($"DisplayInfo 생성 중 오류 발생: {e.Message}");
        }

        return null;
    }

    /// <summary>
    /// 활성화된 디스플레이 정보 목록을 불러오는 함수
    /// </summary>
    /// <returns>활성화된 디스플레이 정보 목록</returns>
    public static List<DisplayInfo> GetDisplayInfos()
    {
        // 반환값 리스트 선언
        List<DisplayInfo> displayInfos = new();

        // 1. 인스턴스 ID 목록 가져오기
        var instanceIds = GetDisplayInstanceIds();

        // 2. 각 인스턴스 ID로 모니터 정보 가져오기
        foreach(string instanceId in instanceIds)
        {
            var displayInfo = GetDisplayInfo(instanceId);
            if (displayInfo is null) continue;

            displayInfos.Add(displayInfo);
        }

        // 3. 리스트 반환하기
        return displayInfos;
     }

    /// <summary>
    /// 목표 인치수를 만들기 위한 가로 세로 cm 길이를 역산한다.
    /// </summary>
    /// <param name="targetInches"></param>
    /// <param name="widthPx"></param>
    /// <param name="heightPx"></param>
    /// <returns></returns>
    public static TargetSize GetTargetSize(double targetInches, int widthPx, int heightPx)
    {
        /* R = Wpx / Hpx            비율
         * D = Inch * 2.54 (cm)     대각선 길이(cm)
         * Wcm = R * Hcm            가로 길이 (cm)
         * D^2 = Wcm^2 + Hcm^2      피타고라스 법칙 (
         * Hcm = D / sqrt(R^2 + 1)
         */
        // 비율 R
        double ratio = (double)Math.Max(1, widthPx) / Math.Max(1, heightPx);
        // 대각선 길이(cm) D
        double diagonalCm = targetInches * 2.54;

        // 세로 길이 원본값 (Round로 인한 오차 누적 방지)
        double rawHeightCm = diagonalCm / Math.Sqrt(ratio * ratio + 1.0);
        // 가로 길이 원본값 (Round로 인한 오차 누적 방지)
        double rawWidthCm = ratio * rawHeightCm;

        // 세로 길이 (cm): 1 바이트 범위인 1 ~ 255 범위로 제한
        int heightCm = Math.Clamp((int)Math.Round(rawHeightCm), 1, 255);
        // 가로 길이 (cm): 1 바이트 범위인 1 ~ 255 범위로 제한
        int widthCm = Math.Clamp((int)Math.Round(rawWidthCm), 1, 255);

        double calcInches = Math.Round(Math.Sqrt(widthCm * widthCm + heightCm * heightCm) / 2.54, 1);

        return new TargetSize(widthCm, heightCm, calcInches);
    }

    /// <summary>
    /// 지정한 모니터 EDID의 가로 세로(cm) 길이를 수정하고 장치를 재시작한다. (관리자 권한 필요)
    /// </summary>
    /// <param name="displayInstanceId"></param>
    /// <param name="widthCm"></param>
    /// <param name="heightCm"></param>
    /// <returns></returns>
    public static bool ApplyEdid(string displayInstanceId, int widthCm, int heightCm)
    {
        
        string edidKeyAddress = $@"{EnumDisplayRoot}\{displayInstanceId}\Device Parameters";
        try
        {
            /* 1. 해당 모니터의 Device Parameter를 쓰기 가능으로 열기 
             *  권한 없으면 Security Exception 던짐
             */
            using var edidKey = Registry.LocalMachine.OpenSubKey(edidKeyAddress, writable: true);
            if (edidKey is null) return false;

            /* 2. edid 바이트 배열이 정상인지 확인 */
            if (edidKey.GetValue("EDID") is not byte[] currentEdid || currentEdid.Length < 128)
                return false;

            byte[] baseEdid;
            // 백업본이 있으면 해당 백업을 복사해 사용
            if (edidKey.GetValue("EDID_BACKUP") is byte[] existBackup && existBackup.Length >= 128)
            {
                baseEdid = (byte[])existBackup.Clone();
            }
            // 백업본이 없으면 새로운 백업을 추가
            else
            {
                baseEdid = (byte[])currentEdid.Clone();
                edidKey.SetValue("EDID_BACKUP", baseEdid, RegistryValueKind.Binary);
            }

            /* 3. edid 수정 시작 */
            byte[] patchedEdid = (byte[])baseEdid.Clone();
            PatchEdidBytes(patchedEdid, widthCm, heightCm);

            /* 4. 수정된 edid를 레지스트리에 저장 */
            /* 4-1. 기본 edid값 갱신 */
            edidKey.SetValue("EDID", patchedEdid, RegistryValueKind.Binary);

            /* 4-2. 재부팅 후에도 유지되도록 EDID_OVERRDE 값 설정 */
            {
                using var edidOverrideKey = edidKey.CreateSubKey("EDID_OVERRIDE");
                /* Window 규격 상, EDID_OVERRIDE\0에 기본 블럭(128바이트)만 잘라서 넣음 */
                byte[] block0 = new byte[128];
                Array.Copy(patchedEdid, 0, block0, 0, 128);
                edidOverrideKey.SetValue("0", block0, RegistryValueKind.Binary);
            }

            /* 5. 수정 사항 반영을 위해 모니터와 터치 키보드 재시작 */
            RestartDisplayAndKeyboard(displayInstanceId);
            return true;
        }
        catch (Exception e)
        {
            // 권한 오류 등...
            Debug.WriteLine($"EDID 수정 중 오류 발생: {e.Message}");
            return false;
        }
    }

    public static bool RestoreEdid(string displayInstanceId)
    {
        /* Device Parameters 주소 */
        string edidKeyAddress = $@"{EnumDisplayRoot}\{displayInstanceId}\Device Parameters";
        try
        {
            /* 1. 해당 모니터의 Device Parameter를 쓰기 가능으로 열기 
             *  권한 없으면 Security Exception 던짐
             */
            using var edidKey = Registry.LocalMachine.OpenSubKey(edidKeyAddress, writable: true);
            if (edidKey is null) return false;

            /* 2. 백업해둔 EDID가 있으면 복원함 */
            if (edidKey.GetValue("EDID_BACKUP") is byte[] backupEdid&& backupEdid.Length >= 128)
            {
                edidKey.SetValue("EDID", backupEdid, RegistryValueKind.Binary);
            }

            /* 3. EDID_OVERRIDE 하위 키 제거 
             * throwOnMissingSubKey 옵션으로 키가 없어도 오류 안 던지게 함 */
            edidKey.DeleteSubKeyTree("EDID_OVERRIDE", throwOnMissingSubKey: false);

            /* 4. 모니터 드라이버 및 터치키보드 재시작 */
            RestartDisplayAndKeyboard(displayInstanceId);
            return true;
        } catch (Exception e)
        {
            Debug.WriteLine($"EDID 복구 중 오류 발생: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// 주어진 edid에 지정한 가로 세로 길이를 적용한다.
    /// </summary>
    /// <param name="edid"></param>
    /// <param name="widthCm"></param>
    /// <param name="heightCm"></param>
    private static void PatchEdidBytes(byte[] edid, int widthCm, int heightCm)
    {
        /* 1. byte로 변환 */
        byte wCm = (byte)Math.Clamp(widthCm, 1, 255);
        byte hCm = (byte)Math.Clamp(heightCm, 1, 255);

        /* 2. 가로세로 물리적 크기 수정 */
        edid[21] = wCm;
        edid[22] = hCm;

        /* 3. DTD1 타이밍 블럭이 있으면 mm 단위 크기로 수정 */
        if (edid.Length >= 72 && (edid[54] != 0 || edid[55] != 0))
        {
            // cm에 10을 곱해 mm로 변환 (12비트 최대값인 4095mm까지 표현 가능)
            int widthMm = Math.Clamp(wCm * 10, 1, 4095);
            int heightMm = Math.Clamp(hCm * 10, 1, 4095);

            // 하위 8비트는 66번(가로), 67번(세로) 칸에 저장
            edid[66] = (byte)(widthMm & 0xFF);
            edid[67] = (byte)(heightMm & 0xFF);

            // 상위 4비트씩을 묶어서 68번 칸 하나에 나눠 저장 (앞 4비트는 가로, 뒤 4비트는 세로)
            edid[68] = (byte)((((widthMm >> 8) & 0x0F) << 4) | ((heightMm >> 8) & 0x0F));
        }

        /* 4. checksum (127) 다시 계산 
         *  0에서 127번 바이트까지 모두 더하고, 이 합이 256으로 나누어 떨어져야 함
         */
        int sum = 0;
        for (int i = 0; i < 127; i++)
        {
            sum += edid[i];
        }
        edid[127] = (byte)((256 - (sum % 256)) % 256);
    }

    /// <summary>
    /// 디스플레이 드라이버와 터치 키보드 프로세스를 재시작한다.
    /// </summary>
    /// <param name="displayInstanceId"></param>
    private static void RestartDisplayAndKeyboard(string displayInstanceId)
    {
        /* 1. pnputil.exe로 해당 모니터를 껐다켠다.
         * pnputil.exe 에 넘길 인자는 "DISPLAY\ABC5D23\1&..." 형태여야 한다.
         */
        try
        {
            string deviceInstanceAddress = $@"DISPLAY\{displayInstanceId}";
            using var process = Process.Start(
                new ProcessStartInfo(
                    "pnputil.exe",
                    $"/restart-device \"{deviceInstanceAddress}\""
                )
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            );
            // 최대 10초까지 대기
            process?.WaitForExit(10000); 
        } 
        catch (Exception e) 
        {
            Debug.WriteLine($"디스플레이 재시작 중 오류 발생: {e.Message}");
        }

        /* 2. Window11 터치 키보드 프로세스(TextInputHost.exe)를 종료시킨다. 
         * 윈도우가 종표된 프로세스를 즉시 다시 실행시켜, 변경된 설정이 적용된다. */
        try
        {
            foreach (var process in Process.GetProcessesByName("TextInputHost"))
            {
                try
                {
                    process.Kill();
                }
                catch (Exception e) 
                {
                    Debug.WriteLine($"터치 키보드 프로세스 종료 중 오류 발생: {e.Message}");
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine($"터치 키보드 프로세스 목록 읽어오는 중 오류 발생: {e.Message}");
        }
    }
}
