using System.Security.Cryptography;
using System.Text;

namespace EasyWin.Models;

/// <summary>远程桌面(mstsc)连接方案。密码经 DPAPI 按当前用户加密存储。</summary>
public class RdpProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    /// <summary>服务器地址,格式 host 或 host:端口(默认 3389)。</summary>
    public string Server { get; set; } = "";

    public string UserName { get; set; } = "";

    /// <summary>DPAPI 加密后的密码(Convert.ToBase64String),仅本机当前用户可解。</summary>
    public string? EncryptedPassword { get; set; }

    public bool RememberPassword { get; set; }

    public bool FullScreen { get; set; } = true;

    public int Width { get; set; } = 1280;

    public int Height { get; set; } = 800;

    /// <summary>管理员会话(/admin)。</summary>
    public bool AdminSession { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string DisplayText => FullScreen ? "全屏" : $"{Width}×{Height}";

    public string Summary
    {
        get
        {
            var mode = FullScreen ? "全屏" : $"{Width}×{Height} 窗口";
            var flags = AdminSession ? " · 管理模式" : "";
            return $"{Server}\n{(string.IsNullOrWhiteSpace(UserName) ? "连接时输入用户名" : UserName)}\n{mode}{flags}";
        }
    }

    // ---------- 密码加解密 ----------

    public void SetPassword(string? plain)
    {
        EncryptedPassword = string.IsNullOrEmpty(plain)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser));
    }

    public string? GetPassword()
    {
        if (string.IsNullOrEmpty(EncryptedPassword)) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(EncryptedPassword), null, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException)
        {
            return null; // 换了用户/系统无法解密
        }
    }
}
