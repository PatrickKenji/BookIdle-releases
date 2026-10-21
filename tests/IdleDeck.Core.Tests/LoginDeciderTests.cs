using IdleDeck.Core.Settings;
using IdleDeck.Core.Vault;

namespace IdleDeck.Core.Tests;

public class LoginDeciderTests
{
    [Theory]
    // método, estado, tem credencial, bloqueada, pode tentar → ação
    [InlineData(LoginMethod.Password, "form", true, false, true, LoginAction.FillPassword)]
    [InlineData(LoginMethod.Password, "form", false, false, true, LoginAction.Nothing)]
    [InlineData(LoginMethod.Google, "form", true, false, true, LoginAction.ClickGoogle)]
    [InlineData(LoginMethod.Google, "form", false, false, true, LoginAction.Nothing)]
    [InlineData(LoginMethod.Google, "google-chooser", true, false, true, LoginAction.PickGoogleAccount)]
    [InlineData(LoginMethod.Google, "google-password", true, false, true, LoginAction.NeedsUser)]
    [InlineData(LoginMethod.Google, "google-identifier", true, false, true, LoginAction.NeedsUser)]
    [InlineData(LoginMethod.Google, "google-blocked", true, false, true, LoginAction.NeedsUser)]
    [InlineData(LoginMethod.Password, "google-chooser", true, false, true, LoginAction.NeedsUser)]
    [InlineData(LoginMethod.None, "form", true, false, true, LoginAction.Nothing)]
    [InlineData(LoginMethod.None, "google-chooser", false, false, true, LoginAction.Nothing)]
    [InlineData(LoginMethod.Password, "form", true, true, true, LoginAction.LockedOut)]
    [InlineData(LoginMethod.Google, "form", true, true, true, LoginAction.LockedOut)]
    [InlineData(LoginMethod.Password, "form", true, false, false, LoginAction.Nothing)]
    [InlineData(LoginMethod.Google, "google-chooser", true, false, false, LoginAction.Nothing)]
    [InlineData(LoginMethod.Google, "ok", true, false, true, LoginAction.LoggedIn)]
    [InlineData(LoginMethod.None, "ok", false, false, true, LoginAction.LoggedIn)]
    [InlineData(LoginMethod.Google, "qualquer-coisa", true, false, true, LoginAction.Nothing)]
    public void Decide(LoginMethod method, string state, bool hasCredential, bool lockedOut, bool canAttempt, LoginAction expected) =>
        Assert.Equal(expected, LoginDecider.Decide(method, state, hasCredential, lockedOut, canAttempt));

    [Theory]
    [InlineData(LoginMethod.Google, "form")]
    [InlineData(LoginMethod.Password, "form")]
    [InlineData(LoginMethod.Google, "google-chooser")]
    public void User_busy_means_nothing(LoginMethod method, string state) =>
        Assert.Equal(LoginAction.Nothing, LoginDecider.Decide(method, state, true, false, true, userBusy: true));

    [Fact]
    public void User_busy_still_records_login_success() =>
        Assert.Equal(LoginAction.LoggedIn, LoginDecider.Decide(LoginMethod.Google, "ok", true, false, true, userBusy: true));
}
