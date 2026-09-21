using System.Net;
using System.Net.Sockets;

namespace EasyWin.Services;

/// <summary>网络唤醒(WOL):向局域网广播 Wake-on-LAN 魔术包,唤醒配置了 MAC 的关机设备。</summary>
public static class WolService
{
    /// <summary>把用户输入的 MAC 规范化为字节与大写冒号格式(容忍 - : 空格分隔与大小写)。</summary>
    public static bool TryNormalizeMac(string? input, out byte[] mac, out string normalized)
    {
        mac = [];
        normalized = "";
        var hex = new string((input ?? "").Where(char.IsLetterOrDigit).ToArray());
        if (hex.Length != 12 || !hex.All(Uri.IsHexDigit)) return false;
        mac = Enumerable.Range(0, 6).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
        normalized = string.Join(":", mac.Select(b => b.ToString("X2")));
        return true;
    }

    /// <summary>广播魔术包:6 字节 0xFF + 16 次目标 MAC,同时发往 9 与 7 端口。</summary>
    public static void SendMagicPacket(byte[] mac)
    {
        var packet = new byte[6 + 16 * 6];
        Array.Fill(packet, (byte)0xFF, 0, 6);
        for (var i = 0; i < 16; i++) Buffer.BlockCopy(mac, 0, packet, 6 + i * 6, 6);

        using var client = new UdpClient();
        client.EnableBroadcast = true;
        client.Send(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, 9));
        client.Send(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, 7));
    }
}
