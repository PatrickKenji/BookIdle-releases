using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdleDeck.Core.Settings;

/// <summary>
/// Lê o layout salvo sem nunca derrubar o arquivo de configurações: o antigo "Free" (modo Livre, removido) vira Grade,
/// e qualquer valor desconhecido vira Abas. Um valor que o <see cref="JsonStringEnumConverter"/> recusasse faria o app
/// descartar o arquivo inteiro, e a pessoa perderia as contas cadastradas.
/// </summary>
public sealed class LayoutModeJsonConverter : JsonConverter<LayoutMode>
{
    public override LayoutMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) return LayoutMode.Tabs;
        var text = reader.GetString();
        if (string.Equals(text, "Free", StringComparison.OrdinalIgnoreCase)) return LayoutMode.Grid2x2;
        return Enum.TryParse<LayoutMode>(text, ignoreCase: true, out var mode) && Enum.IsDefined(mode) ? mode : LayoutMode.Tabs;
    }

    public override void Write(Utf8JsonWriter writer, LayoutMode value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
