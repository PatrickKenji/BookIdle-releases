using System.Text.Json;
using IdleDeck.Core.Modules;

namespace IdleDeck.Core.Tests;

public class ModuleRouterTests
{
    private sealed class FakeModule(string id, bool throws = false) : IModule
    {
        public string Id => id;
        public string Title => id;
        public string UrlPattern => "pokeidle.io";
        public string? InjectedScript => null;
        public List<(int Account, string Data)> Received { get; } = [];

        public void OnMessage(int accountIndex, JsonElement data)
        {
            if (throws) throw new InvalidOperationException("boom");
            Received.Add((accountIndex, data.GetRawText()));
        }
    }

    [Fact]
    public void Routes_to_matching_module()
    {
        var hunt = new FakeModule("hunt");
        var router = new ModuleRouter([new FakeModule("outro"), hunt]);

        var routed = router.Route(3, """{"module":"hunt","data":{"level":42}}""");

        Assert.True(routed);
        Assert.Equal([(3, """{"level":42}""")], hunt.Received);
    }

    [Fact]
    public void Unknown_module_returns_false() =>
        Assert.False(new ModuleRouter([new FakeModule("hunt")]).Route(0, """{"module":"x","data":{}}"""));

    [Fact]
    public void App_messages_return_false() =>
        Assert.False(new ModuleRouter([new FakeModule("hunt")]).Route(0, """{"app":"login","state":"form"}"""));

    [Fact]
    public void Malformed_json_returns_false_without_throwing()
    {
        var router = new ModuleRouter([new FakeModule("hunt")]);

        Assert.False(router.Route(0, "{nada"));
        Assert.False(router.Route(0, "\"texto\""));
    }

    [Fact]
    public void Module_exception_is_swallowed_and_returns_false() =>
        Assert.False(new ModuleRouter([new FakeModule("hunt", throws: true)]).Route(0, """{"module":"hunt","data":{}}"""));

    [Fact]
    public void ModulesFor_matches_host_and_subdomain()
    {
        var router = new ModuleRouter([new FakeModule("hunt")]);

        Assert.Single(router.ModulesFor(new Uri("https://pokeidle.io/play")));
        Assert.Single(router.ModulesFor(new Uri("https://www.pokeidle.io/")));
        Assert.Empty(router.ModulesFor(new Uri("https://example.com")));
        Assert.Empty(router.ModulesFor(new Uri("https://fakepokeidle.io")));
    }

    // ---------- Regra do /hunt: só pokeidle.io e pokeidle.io/app ----------

    private sealed class PathModule(string pattern, params string[] caminhos) : IModule
    {
        public string Id => "hunt";
        public string Title => "hunt";
        public string UrlPattern => pattern;
        public IReadOnlyList<string> AllowedPaths => caminhos;
        public string? InjectedScript => null;
        public void OnMessage(int accountIndex, JsonElement data) { }
    }

    [Theory]
    [InlineData("https://pokeidle.io", true)]
    [InlineData("https://pokeidle.io/", true)]
    [InlineData("https://pokeidle.io/app", true)]
    [InlineData("https://pokeidle.io/app/", true)]
    [InlineData("https://pokeidle.io/app/hunt", false)]
    [InlineData("https://pokeidle.io/wiki", false)]
    [InlineData("https://outro.io/app", false)]
    public void Hunt_only_matches_root_and_app(string url, bool esperado)
    {
        var modulo = new PathModule("pokeidle.io", "/", "/app");
        Assert.Equal(esperado, ModuleRouter.UrlMatches(new Uri(url), modulo));
    }

    [Fact]
    public void Module_without_paths_matches_any_path()
    {
        var modulo = new PathModule("pokeidle.io");

        Assert.True(ModuleRouter.UrlMatches(new Uri("https://pokeidle.io/qualquer/coisa"), modulo));
        Assert.True(ModuleRouter.UrlMatches(new Uri("https://sub.pokeidle.io/x"), modulo));
        Assert.False(ModuleRouter.UrlMatches(new Uri("https://outro.io/x"), modulo));
    }

    [Fact]
    public void Path_matching_ignores_trailing_slash_and_case()
    {
        Assert.True(ModuleRouter.PathMatches("/app/", ["/APP"]));
        Assert.True(ModuleRouter.PathMatches("", ["/"]));
        Assert.False(ModuleRouter.PathMatches("/app/x", ["/app"]));
    }
}
