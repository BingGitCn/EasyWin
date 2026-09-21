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
        // 后台线程(如自动化规则)也会发 Toast,必须回到 UI 线程再触发,
        // 否则处理器操作 WPF 控件会抛跨线程异常
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => ShowRequested?.Invoke(message, type));
            return;
        }
        ShowRequested?.Invoke(message, type);
    }

    public static void Success(string message) => Show(message, ToastType.Success);

    public static void Warning(string message) => Show(message, ToastType.Warning);

    public static void Error(string message) => Show(message, ToastType.Error);
}
