namespace EasyWin.Services;

public enum ToastType
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>全局轻提示:VM 里发消息,主窗口负责展示。</summary>
public static class Toast
{
    public static event Action<string, ToastType>? ShowRequested;

    public static void Show(string message, ToastType type = ToastType.Info)
    {
        Log.Info($"[Toast:{type}] {message}");
        ShowRequested?.Invoke(message, type);
    }

    public static void Success(string message) => Show(message, ToastType.Success);

    public static void Warning(string message) => Show(message, ToastType.Warning);

    public static void Error(string message) => Show(message, ToastType.Error);
}
