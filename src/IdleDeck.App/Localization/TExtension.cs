using System.Windows.Markup;
using IdleDeck.Core.Localization;

namespace IdleDeck.App.Localization;

/// <summary>Texto traduzido no XAML: <c>Content="{l:T 'Salvar'}"</c> (o texto em português é a chave).</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension(string text) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(text);
}
