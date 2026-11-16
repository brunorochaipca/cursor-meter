using Avalonia.Controls;
using Avalonia.Media;

namespace CursorMeter.App;

/// <summary>Version, licence, and the diagnostic log, which can be handed over as a GitHub issue.</summary>
internal sealed partial class AboutWindow : Window
{
    private const int ReportLines = 200;
    private static readonly IBrush Good = new SolidColorBrush(Color.FromRgb(0xA3, 0xBE, 0x8C));
    private static readonly IBrush Warning = new SolidColorBrush(Color.FromRgb(0xEB, 0xCB, 0x8B));

    public AboutWindow()
    {
        InitializeComponent();
        Icon = AppIcon.Create();
        Logo.Source = AppIcon.Render(128);
        VersionText.Text = $"Versão {AppInfo.Version}";
        LogPathText.Text = AppLog.FilePath;

        RepositoryButton.Click += (_, _) =>
        {
            if (!AppInfo.TryOpen(AppInfo.Repository))
                Status("Não consegui abrir o endereço.", Warning);
        };
        FolderButton.Click += (_, _) =>
        {
            Directory.CreateDirectory(AppLog.Folder);
            if (!AppInfo.TryOpen(AppLog.Folder))
                Status("Não consegui abrir a pasta.", Warning);
        };
        SendButton.Click += async (_, _) => await SendAsync();
        CloseButton.Click += (_, _) => Close();
    }

    private async Task SendAsync()
    {
        var log = AppLog.Tail(ReportLines);
        if (log.Length == 0)
        {
            Status("Ainda não há registos para enviar.", Warning);
            return;
        }

        var report = $"CursorMeter {AppInfo.Version}{Environment.NewLine}{AppInfo.Platform}{Environment.NewLine}{Environment.NewLine}{log}";
        try
        {
            await (Clipboard ?? throw new InvalidOperationException("No clipboard")).SetTextAsync(report);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Runtime.InteropServices.COMException)
        {
            AppLog.Error("Could not copy the log to the clipboard", ex);
            OpenIssue();
            Status("Não consegui copiar o registo. Abre a pasta dos registos e anexa o ficheiro à issue.", Warning);
            return;
        }

        if (OpenIssue())
            Status($"Copiei as últimas {ReportLines} linhas do registo. Cola-as na issue que se abriu (Ctrl+V).", Good);
        else
            Status($"Copiei o registo, mas não consegui abrir o navegador. Cola-o numa issue em {AppInfo.Repository}/issues.", Warning);
    }

    private static bool OpenIssue()
    {
        var title = Uri.EscapeDataString($"Registos do CursorMeter {AppInfo.Version}");
        var body = Uri.EscapeDataString(
            $"Versão: {AppInfo.Version}\nSistema: {AppInfo.Platform}\n\nO que aconteceu:\n\n\nRegisto (cola aqui, já está na área de transferência):\n\n```\n\n```\n");
        return AppInfo.TryOpen($"{AppInfo.Repository}/issues/new?title={title}&body={body}");
    }

    private void Status(string message, IBrush color)
    {
        StatusText.Foreground = color;
        StatusText.Text = message;
    }
}
