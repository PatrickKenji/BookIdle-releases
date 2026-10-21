namespace IdleDeck.Core.Health;

/// <summary>Um processo do navegador: memória privada e os frames principais (raiz) que ele desenha.</summary>
public record ProcessSample(long Bytes, IReadOnlyList<ulong> RootFrameIds);

public record MemorySnapshot(long[] AccountBytes, long SharedBytes)
{
    public long TotalBytes => AccountBytes.Sum() + SharedBytes;
}

public static class MemoryAttribution
{
    /// <summary>
    /// Atribui cada processo à conta cujo frame principal ele desenha; o resto
    /// (navegador, GPU, rede, utilitários) vai para "compartilhado".
    /// </summary>
    public static MemorySnapshot Attribute(IEnumerable<ProcessSample> processes, IReadOnlyList<ulong> accountFrameIds)
    {
        var accounts = new long[accountFrameIds.Count];
        long shared = 0;
        foreach (var p in processes)
        {
            var account = -1;
            foreach (var frame in p.RootFrameIds)
            {
                account = IndexOf(accountFrameIds, frame);
                if (account >= 0) break;
            }
            if (account >= 0) accounts[account] += p.Bytes;
            else shared += p.Bytes;
        }
        return new MemorySnapshot(accounts, shared);
    }

    private static int IndexOf(IReadOnlyList<ulong> ids, ulong id)
    {
        for (var i = 0; i < ids.Count; i++)
            if (ids[i] == id) return i;
        return -1;
    }
}
