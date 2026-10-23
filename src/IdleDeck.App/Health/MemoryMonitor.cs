using System.Diagnostics;
using System.Windows.Threading;
using IdleDeck.App.Browser;
using IdleDeck.Core.Health;
using Microsoft.Web.WebView2.Core;

namespace IdleDeck.App.Health;

/// <summary>Mede a cada 15 s a memória privada de cada conta e da base compartilhada do navegador.</summary>
public sealed class MemoryMonitor : IDisposable
{
    private readonly Func<BrowserHost?> _host;
    private readonly Func<IReadOnlyList<AccountView>> _views;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(15) };
    private bool _sampling;

    public MemoryMonitor(Func<BrowserHost?> host, Func<IReadOnlyList<AccountView>> views)
    {
        _host = host;
        _views = views;
        _timer.Tick += async (_, _) => await SampleAsync();
    }

    /// <summary>Contas amostradas (na mesma ordem de <see cref="MemorySnapshot.AccountBytes"/>) e o resultado.</summary>
    public event Action<IReadOnlyList<AccountView>, MemorySnapshot>? Updated;

    public void Start() => _timer.Start();

    public async Task SampleAsync()
    {
        if (_sampling || _host() is not { } host) return;
        _sampling = true;
        try
        {
            var infos = await host.Environment.GetProcessExtendedInfosAsync();
            var samples = infos.Select(i => new ProcessSample(PrivateBytes(i.ProcessInfo.ProcessId), RootFrames(i))).ToList();
            var views = _views().ToList();
            var frameIds = views.Select(v => v.Core is { } c ? (ulong)c.FrameId : ulong.MaxValue).ToList();
            Updated?.Invoke(views, MemoryAttribution.Attribute(samples, frameIds));
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // navegador reiniciando; tenta na próxima amostra
        }
        finally
        {
            _sampling = false;
        }
    }

    /// <summary>Frames principais (de topo) que usam este processo.</summary>
    internal static List<ulong> RootFrames(CoreWebView2ProcessExtendedInfo info)
    {
        var roots = new List<ulong>();
        foreach (var frame in info.AssociatedFrameInfos)
        {
            var root = frame;
            while (root.ParentFrameInfo is { } parent) root = parent;
            roots.Add(root.FrameId);
        }
        return roots;
    }

    private static long PrivateBytes(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.PrivateMemorySize64;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return 0; // processo já saiu
        }
    }

    public void Dispose() => _timer.Stop();
}
