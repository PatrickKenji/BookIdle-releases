using IdleDeck.Core.Health;

namespace IdleDeck.Core.Tests;

public class MemoryAttributionTests
{
    [Fact]
    public void Splits_processes_between_accounts_and_shared()
    {
        // contas com frame principal 10, 20, 30
        var processes = new[]
        {
            new ProcessSample(Bytes: 100, RootFrameIds: []),        // navegador
            new ProcessSample(Bytes: 50, RootFrameIds: []),         // GPU
            new ProcessSample(Bytes: 300, RootFrameIds: [10]),      // conta 0
            new ProcessSample(Bytes: 40, RootFrameIds: [10]),       // iframe de outro site da conta 0
            new ProcessSample(Bytes: 250, RootFrameIds: [30]),      // conta 2
            new ProcessSample(Bytes: 70, RootFrameIds: [999]),      // frame desconhecido
        };

        var snapshot = MemoryAttribution.Attribute(processes, [10, 20, 30]);

        Assert.Equal([340L, 0L, 250L], snapshot.AccountBytes);
        Assert.Equal(220L, snapshot.SharedBytes);
        Assert.Equal(810L, snapshot.TotalBytes);
    }

    [Fact]
    public void Process_counted_once_even_with_many_frames()
    {
        var snapshot = MemoryAttribution.Attribute([new ProcessSample(500, [10, 10, 10])], [10]);

        Assert.Equal([500L], snapshot.AccountBytes);
    }
}
