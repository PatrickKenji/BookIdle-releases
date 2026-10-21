# BookIdle

**🌐 Language / Idioma / Idioma:** [Português](README.md) · **English** · [Español](README.es.md)

**Account manager and tools for [pokeidle.io](https://pokeidle.io)** — several accounts side by side, TopIdle voting, Twitch live
bonuses, hunt ranking and a market sniper. A free, open-source Windows program.

> **Unofficial project.** BookIdle is not affiliated with pokeidle.io, TopIdle, Twitch, Nintendo or The Pokémon Company; all trademarks
> belong to their respective owners. Use it at your own risk and respect the game's rules (see [Responsible use](#responsible-use)).

<p align="center">
  <img src="docs/img/janela-principal.png" alt="BookIdle main window: accounts on the side, with the vote and Twitch buttons, and the game next to them" width="760">
</p>

<p align="center">
  <img src="docs/img/hunt.png" alt="/hunt panel: the best hunt per account and the hunt ranking by XP/h and gold/h" width="760">
</p>

<p align="center">
  <img src="docs/img/sniper.png" alt="Market Sniper panel: new rule, Pokémon radar, active rules, balance protection and feed" width="760">
</p>

<sub>Screenshots use sample data (fictional accounts, balances and hunts). The app itself is available in Portuguese, English and Spanish.</sub>

---

## Download and install

1. Download **[BookIdle-win-Setup.exe](https://github.com/PatrickKenji/BookIdle-releases/releases/latest/download/BookIdle-win-Setup.exe)**
   (or see every version under [Releases](https://github.com/PatrickKenji/BookIdle-releases/releases)).
2. Run the installer. It installs for your user only (no administrator rights needed) and installs WebView2 if it is missing.
3. The first time, Windows may show **"Windows protected your PC"**: the installer does not have a paid digital signature. Click
   **More info → Run anyway**. If you prefer, the source code is right here for you to inspect and build yourself.

BookIdle **updates itself**: it checks for new versions when it opens and every hour, tells you, and updates with one click, downloading
only what changed.

Requires **Windows 10 or 11**.

---

## Privacy and security

**BookIdle's authors have no access to your accounts, passwords or data.** There is no server of ours: the app runs entirely on your
computer and **nothing is sent to us**.

- **Everything stays on your PC**, in the `%LOCALAPPDATA%\BookIdleData` folder: settings, rules, account logins and passwords.
- **Each account has its own isolated browser** (WebView2): one account's sessions and cookies never mix with the other accounts' or with
  your regular browser.
- **Optional password vault**, encrypted on your PC: with your Windows user (DPAPI) or with a **master password** (AES-256-GCM, key
  derived with PBKDF2 and 600,000 iterations). For **Google** accounts, BookIdle never types your Google password: at most it picks the
  account from the list, and the rest is up to you.
- **No telemetry, no usage analytics, no error reports sent.** The only addresses the app contacts are:
  - `pokeidle.io` — the game itself and the public item list (`/assets/items.json`);
  - `topidle.com` — the voting page, in the vote window (you do the voting; the app clicks nothing);
  - `twitch.tv` and `irc-ws.chat.twitch.tv` — only if you connect Twitch (see below);
  - `github.com` — to check for a new version and download it;
  - `livepix.gg` — only if you click "Support".
- **The code is open**: anyone can read and verify all of this here. Download BookIdle only from this page.

**About Twitch:** you sign in to Twitch on the Twitch website itself, inside the account's browser. The app uses the login that stays
there (on your PC) to join the bonus channels' chats with a lightweight connection, **without opening a browser per channel**. That login
is only ever sent to Twitch.

---

## Features for pokeidle.io

### Accounts side by side
- **Several accounts** in the same program, each in an isolated browser, with each one's status (online, memory) in view.
- **Three window modes:** **Tabs**, **Grid** (all accounts visible) and **1+N** (one large and the others small). Shortcuts: `Ctrl+1` to
  `Ctrl+3`.
- **Drag to reorder** accounts, in the side list and in the windows.
- **Expand button (⛶)** on each window: switches to Tabs mode already on the chosen account.
- **Sound:** mute everything, or mute each account (🔊/🔇 in each window's header and in the account menu).
- **Theme** Arcade (purple) or Dark (`Ctrl+T`), **collapsible menu** (`Ctrl+B`) and per-profile game **zoom**.
- **Windows tray:** minimizing sends the app to the tray; option to **start with Windows** already minimized.
- **Memory under control:** shows each account's RAM, automatically reloads the one that goes over the limit you set, and has a
  **Reset profile** button. Hidden accounts are not slowed down (option).
- **Ad and tracker blocking**, configurable by domain list.
- **Optional automatic login:** fills in the game login from the Vault and, on Google, picks the account from the list.
- **Game profiles:** besides PokeIdle you can create profiles for other web games (see [Other games](#other-idle-games)).

### Vote on TopIdle
- A **"Vote on TopIdle"** button on each account, which opens the voting page **with that account's session**. You vote; BookIdle **does
  not click for you**.
- The button shows **how long is left** until you can vote again ("Vote in 5 h"), reading what the game itself reports — it even works
  for votes made outside the app — and lights up yellow when you can vote.

### Twitch live bonuses
- **Connect Twitch** on each account (each with its own Twitch), with the profile's list of bonus channels.
- The account stays present in the channels' chats **without you opening any stream** and without spending a browser's memory per channel.
- Status icon per account (connected to all channels, partial, no key).

### `/hunt` — which hunt pays best
- Shows, **for the Pokémon on the field in each account**, the best hunts by **XP/h** and by **net gold/h** (already discounting the
  potions, revives and balls the account spends), with each one's risk and the next ones you unlock.
- Uses **the game's own formulas and catalog, live** — when the game changes, the calculation follows.
- Understands the **seasonal day** (the day's type doubles attack, defense and HP in the map hunt) and lets you adjust the TM AoE hits per target.

### Market Sniper
- Automatically buys, **in the background**, Community Market offers that match your rules — without you watching the market. The
  accounts keep playing normally.
- **Per-account rules** (up to 15): pick the item or Pokémon in a search, set the **coin cap** and the **gem cap**, and the quantity.
- **Global Pokémon radar:** any **shiny** (gems only), any **P5** or **minimum rating**, with your caps.
- **Balance protection:** a coin and gem reserve the sniper never spends.
- Respects the market's **2-minute hold** and **draw**: no cutting in line. Buys one unit per order and only confirms after the game's
  notice; shows everything in a per-account **event feed** and writes a `sniper.log`.
- Choose **which accounts** do the sniping (one or all); two accounts never compete for the same offer.

### Also
- **Automatic updates** (above) and a discreet **support button** in the menu.

---

## Responsible use

- **Respect the game's rules.** pokeidle.io limits accounts per person (currently **4 per person**, across all computers and networks)
  and punishes those who go over. BookIdle lets you create more per profile; **the limit is yours, not the program's**. Read the game's terms.
- The game allows automation that follows the same rules as someone playing by hand (no exploiting bugs and no overloading the server).
  The sniper was built that way; still, **you are responsible for how you use it**.
- **The sniper spends real in-game coins and gems.** Start with small rules and a high balance reserve, and watch the feed. A purchase
  made at the wrong price is not undone by the game.
- No warranty: the program is provided "as is" (see the [license](LICENSE)). The game changes now and then and something may break until
  someone fixes it — open an issue.

---

## Other idle games

BookIdle already opens **any web game in profiles** (each profile with its own account limit, windows, memory, tray and login), so it
**works today as a multi-account manager for other idle games**. The special tools (vote, `/hunt`, sniper, Twitch) are for pokeidle.io.

**We will work on tools for other idle games if people want them.** Tell us which game and what would make a difference for you by opening
an [issue](https://github.com/PatrickKenji/BookIdle-releases/issues) or a conversation in
[Discussions](https://github.com/PatrickKenji/BookIdle-releases/discussions).

---

## Build and contribute

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and Windows (the app is WPF + WebView2).

```powershell
dotnet build          # builds everything
```

| Folder | What is in it |
|---|---|
| `src/IdleDeck.Core` | pure logic, no UI: settings, layouts, vault, Twitch, vote, market, sniper |
| `src/IdleDeck.App` | the program (WPF + WebView2), the `/hunt` and Sniper modules and the scripts injected into pages |
| `docs/` | [market protocol](docs/pokeidle-mercado.md), [`/hunt` notes](docs/pokeidle-notes.md) (in Portuguese) |

Suggestions and problem reports are welcome: open an [issue](https://github.com/PatrickKenji/BookIdle-releases/issues) or join the
[Discussions](https://github.com/PatrickKenji/BookIdle-releases/discussions). To keep updates safe, **only the project maintainer changes
the code** (outside pull requests are turned off); you can read, copy and modify freely for your own use, within the [license](LICENSE).

---

## License and credits

[MIT](LICENSE) — © 2026 Tickelicia. You may use, copy, modify and distribute.

Made with [.NET](https://dotnet.microsoft.com), [WPF](https://learn.microsoft.com/dotnet/desktop/wpf/), Microsoft's
[WebView2](https://developer.microsoft.com/microsoft-edge/webview2/) and [Velopack](https://velopack.io) (installer and
updates). Fonts: *Press Start 2P* and *Space Grotesk* (OFL license, in `src/IdleDeck.App/Resources/Fonts`).

Enjoying it? The **♥ Support** button in the app menu leads to the [support page](https://livepix.gg/tickelicia).
