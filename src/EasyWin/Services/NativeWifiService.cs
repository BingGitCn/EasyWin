using System.Runtime.InteropServices;

namespace EasyWin.Services;

/// <summary>通过原生 wlanapi 触发 WLAN 主动扫描,刷新系统缓存的网络列表。</summary>
public static class NativeWifiService
{
    private const uint ClientVersion = 2;
    private const int HeaderSize = 8;   // 列表头:数量(4) + 索引(4)
    private const int ItemSize = 532;   // GUID(16) + 描述[256](512) + 状态(4)

    /// <summary>对所有 WLAN 接口触发一次主动扫描,扫描在系统后台进行,约需 2-4 秒。</summary>
    public static void TriggerScan()
    {
        var client = IntPtr.Zero;
        var listPtr = IntPtr.Zero;
        try
        {
            if (WlanOpenHandle(ClientVersion, IntPtr.Zero, out _, out client) != 0) return;
            if (WlanEnumInterfaces(client, IntPtr.Zero, out listPtr) != 0) return;

            var count = Marshal.ReadInt32(listPtr, 0);
            for (var i = 0; i < count; i++)
            {
                var guidBytes = new byte[16];
                Marshal.Copy(new IntPtr(listPtr.ToInt64() + HeaderSize + i * ItemSize), guidBytes, 0, 16);
                var guid = new Guid(guidBytes);
                WlanScan(client, ref guid, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("触发 WLAN 扫描失败: " + ex.Message);
        }
        finally
        {
            if (listPtr != IntPtr.Zero) WlanFreeMemory(listPtr);
            if (client != IntPtr.Zero) WlanCloseHandle(client, IntPtr.Zero);
        }
    }

    [DllImport("wlanapi.dll")]
    private static extern int WlanOpenHandle(uint dwClientVersion, IntPtr pReserved, out uint pdwNegotiatedVersion, out IntPtr phClientHandle);

    [DllImport("wlanapi.dll")]
    private static extern int WlanCloseHandle(IntPtr hClientHandle, IntPtr pReserved);

    [DllImport("wlanapi.dll")]
    private static extern int WlanFreeMemory(IntPtr pData);

    [DllImport("wlanapi.dll")]
    private static extern int WlanEnumInterfaces(IntPtr hClientHandle, IntPtr pReserved, out IntPtr ppInterfaceList);

    [DllImport("wlanapi.dll")]
    private static extern int WlanScan(IntPtr hClientHandle, ref Guid interfaceGuid, IntPtr pDot11Ssid, IntPtr pIeData, IntPtr pReserved);
}
