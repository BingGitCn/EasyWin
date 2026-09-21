namespace EasyWin.Models;

/// <summary>快捷启动条目。</summary>
public class LaunchItem
{
    public LaunchItem(string name, string description, string symbol, string command, string arguments = "", bool requiresPro = false)
    {
        Name = name;
        Description = description;
        Symbol = symbol;
        Command = command;
        Arguments = arguments;
        RequiresPro = requiresPro;
    }

    public string Name { get; }

    public string Description { get; }

    /// <summary>WPF-UI SymbolRegular 图标名。</summary>
    public string Symbol { get; }

    public string Command { get; }

    public string Arguments { get; }

    /// <summary>家庭版不可用(如组策略编辑器)。</summary>
    public bool RequiresPro { get; }
}

/// <summary>快捷启动分组。</summary>
public class LaunchGroup
{
    public LaunchGroup(string title, string symbol, IReadOnlyList<LaunchItem> items)
    {
        Title = title;
        Symbol = symbol;
        Items = items;
    }

    public string Title { get; }

    public string Symbol { get; }

    public IReadOnlyList<LaunchItem> Items { get; }
}
