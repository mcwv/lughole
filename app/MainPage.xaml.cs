namespace Lughole;

public partial class MainPage : ContentPage
{
    const string HostKey = "pc-host";

    CancellationTokenSource? _cts;

    public MainPage()
    {
        InitializeComponent();
        HostEntry.Text = Preferences.Get(HostKey, "");
    }

    async void OnConnectClicked(object? sender, EventArgs e)
    {
        if (_cts != null)
        {
            _cts.Cancel();
            return;
        }

        var host = HostEntry.Text?.Trim() ?? "";
        if (host.Length == 0)
        {
            StatusLabel.Text = "Type in the address the PC server printed.";
            return;
        }

        Preferences.Set(HostKey, host);
        HostEntry.IsEnabled = false;
        ConnectButton.Text = "Stop";
        _cts = new CancellationTokenSource();

        try
        {
            var receiver = new AudioReceiver(status =>
                MainThread.BeginInvokeOnMainThread(() => StatusLabel.Text = status));
            var token = _cts.Token;
            await Task.Run(() => receiver.RunAsync(host, AudioReceiver.DefaultPort, token));
            StatusLabel.Text = "Stopped.";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error: {ex.Message}";
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            HostEntry.IsEnabled = true;
            ConnectButton.Text = "Listen";
        }
    }
}
