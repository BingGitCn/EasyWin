using System.Windows;
using System.Windows.Input;
using EasyWin.Services;
using Wpf.Ui.Controls;

namespace EasyWin.Views;

/// <summary>通用单行文本输入对话框(如"保存代理方案"命名),返回用户输入或 null(取消)。</summary>
public partial class InputWindow : FluentWindow
{
    /// <summary>用户点击确定后的输入结果;取消为 null。</summary>
    public string? Result { get; private set; }

    private InputWindow(string title, string prompt, string initialValue)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        ValueBox.Text = initialValue;
        Loaded += (_, _) => { ValueBox.Focus(); ValueBox.SelectAll(); };
    }

    /// <summary>展示输入对话框并等待结果。</summary>
    public static string? Show(Window owner, string title, string prompt, string initialValue = "")
    {
        var window = new InputWindow(title, prompt, initialValue) { Owner = owner };
        window.ShowDialog();
        return window.Result;
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => Confirm();

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private void OnValueKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Confirm();
    }

    private void Confirm()
    {
        var text = ValueBox.Text.Trim();
        if (text.Length == 0)
        {
            Toast.Warning("内容不能为空");
            return;
        }
        Result = text;
        DialogResult = true;
        Close();
    }
}
