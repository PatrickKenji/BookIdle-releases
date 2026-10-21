using IdleDeck.Core.Settings;

namespace IdleDeck.Core.Vault;

public enum LoginAction { Nothing, FillPassword, ClickGoogle, PickGoogleAccount, NeedsUser, LockedOut, LoggedIn }

/// <summary>
/// Decide o que fazer quando a página de uma conta avisa um estado de login.
/// Estados: <c>form</c> (tela de login do jogo), <c>ok</c> (logado), e no Google
/// <c>google-chooser</c>, <c>google-password</c>, <c>google-identifier</c>, <c>google-blocked</c>.
/// O app nunca preenche nada no Google: só escolhe a conta no seletor; o resto vira "precisa de você".
/// </summary>
public static class LoginDecider
{
    /// <param name="userBusy">O usuário está resolvendo o login na mão ("Precisa de você"): o app não mexe.</param>
    public static LoginAction Decide(LoginMethod method, string state, bool hasCredential, bool lockedOut, bool canAttempt,
        bool userBusy = false)
    {
        if (state == "ok") return LoginAction.LoggedIn;
        if (method == LoginMethod.None || userBusy) return LoginAction.Nothing;

        if (state.StartsWith("google-", StringComparison.Ordinal))
        {
            if (method != LoginMethod.Google) return LoginAction.NeedsUser; // conta de senha caiu no Google
            if (state != "google-chooser") return LoginAction.NeedsUser;    // senha, e-mail, 2FA ou bloqueio
            if (!hasCredential) return LoginAction.NeedsUser;
            return canAttempt ? LoginAction.PickGoogleAccount : LoginAction.Nothing;
        }

        if (state != "form") return LoginAction.Nothing;
        if (lockedOut) return LoginAction.LockedOut;
        if (!hasCredential || !canAttempt) return LoginAction.Nothing;
        return method == LoginMethod.Password ? LoginAction.FillPassword : LoginAction.ClickGoogle;
    }
}
