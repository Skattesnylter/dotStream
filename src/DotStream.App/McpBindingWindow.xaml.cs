using System.Windows;
using System.Windows.Controls;
using DotStream.App.Mcp;

namespace DotStream.App;

/// <summary>
/// Configures one key to call one MCP tool.
///
/// The tool list is fetched from the server rather than typed, which is the whole
/// reason this is pleasant to use: connect, see what is there, pick one. Typing tool
/// names from memory is how you get a key that silently does nothing.
/// </summary>
public partial class McpBindingWindow : Window
{
    private readonly McpClient _client;

    public McpBindingWindow(McpClient client, McpBinding? existing)
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);

        _client = client;

        if (existing is null) return;

        UrlBox.Text = existing.Url;
        ArgumentsBox.Text = existing.Arguments;
        LabelBox.Text = existing.Label;
        _wantedTool = existing.Tool;
    }

    private string? _wantedTool;

    public McpBinding? Result { get; private set; }

    private async void OnConnect(object sender, RoutedEventArgs e) => await ConnectAsync();

    private async Task ConnectAsync()
    {
        string url = UrlBox.Text.Trim();

        if (url.Length == 0)
        {
            ResultLabel.Text = "Enter a server URL first.";
            return;
        }

        ResultLabel.Text = "Connecting...";
        ToolList.ItemsSource = null;

        try
        {
            IReadOnlyList<McpToolInfo> tools = await _client.ListToolsAsync(url);
            ToolList.ItemsSource = tools;

            ResultLabel.Text = tools.Count == 0
                ? "The server answered but offers no tools."
                : $"{tools.Count} tool(s) available.";

            if (_wantedTool is not null)
            {
                ToolList.SelectedItem = tools.FirstOrDefault(t => t.Name == _wantedTool);
                _wantedTool = null;
            }
        }
        catch (Exception ex)
        {
            ResultLabel.Text = "Could not reach the server: " + ex.Message;
        }
    }

    private void OnToolSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ToolList.SelectedItem is not McpToolInfo tool)
        {
            SaveButton.IsEnabled = false;
            return;
        }

        SaveButton.IsEnabled = true;

        if (LabelBox.Text.Trim().Length == 0) LabelBox.Text = tool.Name;
        if (ArgumentsBox.Text.Trim().Length == 0) ArgumentsBox.Text = "{}";
    }

    private async void OnTest(object sender, RoutedEventArgs e)
    {
        if (Build() is not { } binding)
        {
            ResultLabel.Text = "Pick a tool first.";
            return;
        }

        ResultLabel.Text = "Calling " + binding.Tool + "...";

        try
        {
            McpCallResult result = await _client.CallAsync(binding.Url, binding.Tool, binding.Arguments);
            ResultLabel.Text = (result.IsError ? "Tool reported an error: " : "") + result.Text;
        }
        catch (Exception ex)
        {
            ResultLabel.Text = "Call failed: " + ex.Message;
        }
    }

    private McpBinding? Build()
    {
        if (ToolList.SelectedItem is not McpToolInfo tool) return null;

        return new McpBinding
        {
            Url = UrlBox.Text.Trim(),
            Tool = tool.Name,
            Arguments = ArgumentsBox.Text.Trim(),
            Label = LabelBox.Text.Trim()
        };
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (Build() is not { } binding) return;

        Result = binding;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
