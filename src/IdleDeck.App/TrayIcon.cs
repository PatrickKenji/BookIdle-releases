using System.Drawing;
using Forms = System.Windows.Forms;

namespace IdleDeck.App;

/// <summary>Ícone na bandeja: Abrir, Resetar tudo, Sair. Duplo clique abre.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;

    public TrayIcon(Action open, Action resetAll, Action exit, Action checkUpdates, string version)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(new Forms.ToolStripMenuItem($"BookIdle {version}") { Enabled = false });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Loc.T("Abrir"), null, (_, _) => open());
        menu.Items.Add(Loc.T("Resetar tudo"), null, (_, _) => resetAll());
        menu.Items.Add(Loc.T("Verificar atualizações"), null, (_, _) => checkUpdates());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Loc.T("Sair"), null, (_, _) => exit());

        _icon = new Forms.NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application,
            Text = $"BookIdle {version}",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => open();
    }

    /// <summary>Balão de aviso do Windows (some sozinho).</summary>
    public void Notify(string title, string text) => _icon.ShowBalloonTip(5000, title, text, Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
