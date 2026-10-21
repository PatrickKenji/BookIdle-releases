using System.Text;
using IdleDeck.Core.Vault;

namespace IdleDeck.Core.Tests;

public class VaultStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("idledeck-vault-").FullName;
    private string FilePath => Path.Combine(_dir, "vault.dat");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static Dictionary<string, Credential> Sample() => new()
    {
        ["conta1"] = new Credential("treinador@exemplo.com", "senhaSecreta123"),
        ["conta2"] = new Credential("outro", "x"),
    };

    [Fact]
    public void Dpapi_roundtrip()
    {
        var store = new VaultStore(FilePath);
        store.Save(Sample(), masterPassword: null);

        var loaded = store.Unlock(null);

        Assert.Equal(Sample(), loaded);
    }

    [Fact]
    public void Password_roundtrip()
    {
        var store = new VaultStore(FilePath);
        store.Save(Sample(), "mestra");

        Assert.Equal(Sample(), store.Unlock("mestra"));
    }

    [Fact]
    public void Wrong_password_throws_VaultUnlockException()
    {
        var store = new VaultStore(FilePath);
        store.Save(Sample(), "mestra");

        Assert.Throws<VaultUnlockException>(() => store.Unlock("errada"));
    }

    [Fact]
    public void Corrupt_file_throws_VaultUnlockException()
    {
        File.WriteAllBytes(FilePath, [2, 1, 2, 3]);

        Assert.Throws<VaultUnlockException>(() => new VaultStore(FilePath).Unlock("x"));
    }

    [Fact]
    public void File_does_not_contain_plaintext()
    {
        foreach (var master in new[] { null, "mestra" })
        {
            new VaultStore(FilePath).Save(Sample(), master);
            var bytes = File.ReadAllBytes(FilePath);

            Assert.False(Contains(bytes, Encoding.UTF8.GetBytes("senhaSecreta123")));
            Assert.False(Contains(bytes, Encoding.UTF8.GetBytes("treinador@exemplo.com")));
        }
    }

    [Fact]
    public void RequiresMasterPassword_reflects_mode()
    {
        var store = new VaultStore(FilePath);
        Assert.False(store.Exists);

        store.Save(Sample(), null);
        Assert.True(store.Exists);
        Assert.False(store.RequiresMasterPassword);

        store.Save(Sample(), "mestra");
        Assert.True(store.RequiresMasterPassword);
    }

    [Fact]
    public void Delete_removes_vault()
    {
        var store = new VaultStore(FilePath);
        store.Save(Sample(), "mestra");

        store.Delete();

        Assert.False(store.Exists);
        store.Delete(); // apagar de novo não falha
    }

    private static bool Contains(byte[] haystack, byte[] needle) =>
        haystack.AsSpan().IndexOf(needle) >= 0;
}
