using System.Windows;

namespace Sarmat.TelemetryMonitor;

public partial class SettingsWindow : Window
{
    public string AggregatorUrl => UrlBox.Text.Trim();
    public string Secret => SecretBox.Text.Trim();
    public int ReconnectSeconds { get; private set; }

    public SettingsWindow(string aggregatorUrl, string secret, int reconnectSeconds)
    {
        InitializeComponent();
        UrlBox.Text = aggregatorUrl;
        SecretBox.Text = secret;
        ReconnectBox.Text = reconnectSeconds.ToString();
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(AggregatorUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "ws" && uri.Scheme != "wss"))
        {
            ErrorText.Text = "Aggregator URL must start with ws:// or wss://.";
            return;
        }
        if (string.IsNullOrWhiteSpace(Secret))
        {
            ErrorText.Text = "Secret is required.";
            return;
        }
        if (!int.TryParse(ReconnectBox.Text, out var reconnect) || reconnect < 1 || reconnect > 300)
        {
            ErrorText.Text = "Reconnect interval must be between 1 and 300 seconds.";
            return;
        }
        ReconnectSeconds = reconnect;
        DialogResult = true;
    }
}
