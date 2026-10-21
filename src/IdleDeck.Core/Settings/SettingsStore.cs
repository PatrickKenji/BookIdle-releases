using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace IdleDeck.Core.Settings;

public class SettingsStore(string filePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Nunca lança: arquivo ausente, ilegível ou inválido resulta nas configurações padrão. Aceita a versão 1.</summary>
    public AppSettings Load()
    {
        AppSettings? settings;
        try
        {
            if (!File.Exists(filePath)) return AppSettings.CreateDefault();
            var node = JsonNode.Parse(File.ReadAllText(filePath));
            if (node is JsonObject root && IsVersion1(root)) node = MigrateFromVersion1(root);
            settings = node.Deserialize<AppSettings>(JsonOptions);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return AppSettings.CreateDefault(); // arquivo preso por outro programa: não mexe nele
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            settings = null;
        }

        if (settings is not null && settings.Normalize()) return settings;

        try { File.Move(filePath, filePath + ".bad", overwrite: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return AppSettings.CreateDefault();
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        var tmp = filePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tmp, filePath, overwrite: true);
    }

    private static bool IsVersion1(JsonObject root) =>
        !root.ContainsKey("Profiles") && root.ContainsKey("Accounts");

    /// <summary>
    /// Versão 1: lista plana de contas (cada uma com Url e AutoLoginEnabled) + Layout/FocusedAccount globais.
    /// Vira um perfil "PokeIdle" com as mesmas contas e os mesmos perfis de navegador.
    /// </summary>
    private static JsonObject MigrateFromVersion1(JsonObject v1)
    {
        var accounts = new JsonArray();
        string? url = null;
        foreach (var node in v1["Accounts"] as JsonArray ?? [])
        {
            if (node is not JsonObject a)
            {
                accounts.Add(null);
                continue;
            }
            url ??= a["Url"]?.GetValue<string>();
            var autoLogin = a["AutoLoginEnabled"]?.GetValue<bool>() ?? false;
            accounts.Add(new JsonObject
            {
                ["Name"] = a["Name"]?.DeepClone(),
                ["ProfileName"] = a["ProfileName"]?.DeepClone(),
                ["Login"] = autoLogin ? nameof(LoginMethod.Password) : nameof(LoginMethod.None),
            });
        }

        var profile = new JsonObject
        {
            ["Id"] = AppSettings.DefaultProfileId,
            ["Name"] = "PokeIdle",
            ["Url"] = url ?? AppSettings.DefaultUrl,
            ["MaxAccounts"] = Math.Clamp(accounts.Count, 4, GameProfile.MaxAccountsLimit),
            ["Accounts"] = accounts,
        };
        if (v1["Layout"] is { } layout) profile["Layout"] = layout.DeepClone();
        if (v1["FocusedAccount"] is { } focused) profile["FocusedAccount"] = focused.DeepClone();

        var v2 = new JsonObject();
        foreach (var (key, value) in v1)
            if (key is not ("Accounts" or "Layout" or "FocusedAccount"))
                v2[key] = value?.DeepClone();
        v2["Version"] = AppSettings.CurrentVersion;
        v2["Profiles"] = new JsonArray(profile);
        v2["SelectedProfileId"] = AppSettings.DefaultProfileId;
        return v2;
    }
}
