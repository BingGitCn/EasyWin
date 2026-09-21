using System.Text;
using System.Management;
using System.Runtime.InteropServices;

namespace EasyWin.Services;

/// <summary>安全弹出可移动磁盘:定位盘符对应的物理设备,通过配置管理器请求弹出(与系统"弹出"等效)。</summary>
public static class UsbEjectService
{

    public static (bool ok, string message) Eject(string driveLetter)
    {
        var letter = driveLetter.TrimEnd('\\');
        try
        {
            // 盘符 → 分区 → 物理磁盘
            using var partitions = new ManagementObjectSearcher(
                $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{letter}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");
            foreach (var partition in partitions.Get())
            {
                var partitionDeviceId = (string)partition["DeviceID"];
                using var disks = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partitionDeviceId}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");
                foreach (var disk in disks.Get())
                {
                    var pnpId = disk["PNPDeviceID"] as string;
                    if (string.IsNullOrEmpty(pnpId)) continue;

                    var (ok, message) = EjectByInstanceId(pnpId);
                    if (ok) Log.Info($"已安全弹出 {letter} ({pnpId})");
                    return (ok, message);
                }
            }
            return (false, "未找到该盘符对应的可移动磁盘设备");
        }
        catch (Exception ex)
        {
            Log.Error("安全弹出失败", ex);
            return (false, "弹出失败:" + ex.Message);
        }
    }

    private static (bool ok, string message) EjectByInstanceId(string instanceId)
    {
        var status = 0;
        var problem = 0;
        if (CM_Get_DevNode_Status(out status, out problem, InstanceIdToDevNode(instanceId), 0) != CR_SUCCESS)
            return (false, "无法读取设备状态");
        // 内部盘(NVMe/SATA)拒绝弹出;可移动或 USB 外接盘放行(调用方已按 IsRemovable 过滤)
        if (instanceId.StartsWith("NVME\\", StringComparison.OrdinalIgnoreCase))
            return (false, "系统盘不支持弹出");

        if (CM_Get_Parent(out var parent, InstanceIdToDevNode(instanceId), 0) != CR_SUCCESS)
            return (false, "无法定位父设备");

        var vetoName = new StringBuilder(512);
        var veto = 0;
        var result = CM_Request_Device_EjectW(parent, out veto, vetoName, vetoName.Capacity, 0);
        return result == CR_SUCCESS
            ? (true, "已安全弹出")
            : (false, $"系统拒绝了弹出请求{(vetoName.Length > 0 ? ":" + vetoName : "")}(可能有程序正在使用该磁盘)");
    }

    private static int InstanceIdToDevNode(string instanceId)
    {
        if (CM_Locate_DevNodeW(out var dev, instanceId, 0) != CR_SUCCESS)
            throw new InvalidOperationException("设备节点定位失败");
        return dev;
    }

    private const int CR_SUCCESS = 0;

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out int pdnDevInst, string pDeviceID, int ulFlags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_DevNode_Status(out int pulStatus, out int pulProblemNumber, int dnDevInst, int ulFlags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Parent(out int pdnDevInst, int dnDevInst, int ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Request_Device_EjectW(int dnDevInst, out int pVetoType, StringBuilder pszVetoName, int ulNameLength, int ulFlags);
}
