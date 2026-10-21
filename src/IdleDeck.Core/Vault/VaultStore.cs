using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IdleDeck.Core.Vault;

/// <summary>
/// Cofre local de credenciais por perfil de conta.
/// Formato: 1 byte de modo + payload.
///   1 = DPAPI (usuário atual do Windows)
///   2 = senha mestra: [salt 16][nonce 12][tag 16][ciphertext], PBKDF2-SHA256 600k → AES-256-GCM
/// </summary>
public class VaultStore(string filePath)
{
    private const byte ModeDpapi = 1;
    private const byte ModePassword = 2;
    private const int SaltSize = 16, NonceSize = 12, TagSize = 16, KeySize = 32;
    private const int Iterations = 600_000;
    // NÃO MUDAR: é parte da chave do cofre. Trocar este texto impediria abrir os cofres já criados (o nome vem da versão antiga do app).
    private static readonly byte[] DpapiEntropy = Encoding.UTF8.GetBytes("IdleDeckPessoal");

    public bool Exists => File.Exists(filePath);

    public bool RequiresMasterPassword
    {
        get
        {
            if (!Exists) return false;
            using var fs = File.OpenRead(filePath);
            return fs.ReadByte() == ModePassword;
        }
    }

    public Dictionary<string, Credential> Unlock(string? masterPassword)
    {
        byte[] data;
        try
        {
            data = File.ReadAllBytes(filePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new VaultUnlockException(Loc.T("Não foi possível ler o cofre."), e);
        }

        try
        {
            var json = data.Length == 0 ? throw new VaultUnlockException(Loc.T("Cofre vazio.")) : data[0] switch
            {
                ModeDpapi => ProtectedData.Unprotect(data[1..], DpapiEntropy, DataProtectionScope.CurrentUser),
                ModePassword => DecryptWithPassword(data, masterPassword ?? ""),
                _ => throw new VaultUnlockException(Loc.T("Formato de cofre desconhecido.")),
            };
            return JsonSerializer.Deserialize<Dictionary<string, Credential>>(json)
                   ?? throw new VaultUnlockException(Loc.T("Cofre vazio."));
        }
        catch (VaultUnlockException)
        {
            throw;
        }
        catch (Exception e) when (e is CryptographicException or JsonException or ArgumentException)
        {
            throw new VaultUnlockException(Loc.T("Senha mestra incorreta ou cofre corrompido."), e);
        }
    }

    public void Save(Dictionary<string, Credential> byProfile, string? masterPassword)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(byProfile);
        byte[] output;
        if (string.IsNullOrEmpty(masterPassword))
        {
            output = [ModeDpapi, .. ProtectedData.Protect(json, DpapiEntropy, DataProtectionScope.CurrentUser)];
        }
        else
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var tag = new byte[TagSize];
            var cipher = new byte[json.Length];
            using (var aes = new AesGcm(DeriveKey(masterPassword, salt), TagSize))
                aes.Encrypt(nonce, json, cipher, tag);
            output = [ModePassword, .. salt, .. nonce, .. tag, .. cipher];
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        var tmp = filePath + ".tmp";
        File.WriteAllBytes(tmp, output);
        File.Move(tmp, filePath, overwrite: true);
    }

    /// <summary>Apaga o cofre (para quem esqueceu a senha mestra).</summary>
    public void Delete() => File.Delete(filePath);

    private static byte[] DecryptWithPassword(byte[] data, string password)
    {
        const int header = 1 + SaltSize + NonceSize + TagSize;
        if (data.Length < header) throw new VaultUnlockException(Loc.T("Cofre corrompido."));

        var salt = data.AsSpan(1, SaltSize);
        var nonce = data.AsSpan(1 + SaltSize, NonceSize);
        var tag = data.AsSpan(1 + SaltSize + NonceSize, TagSize);
        var cipher = data.AsSpan(header);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(DeriveKey(password, salt.ToArray()), TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    private static byte[] DeriveKey(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
}
