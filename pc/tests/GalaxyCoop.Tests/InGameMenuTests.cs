using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using GalaxyCoop.Companion;
using GalaxyCoop.Launcher;
using GalaxyCoop.Protocol;

namespace GalaxyCoop.Tests;

/// <summary>The in-game menu protocol against a fake game: requests in, status and players out.</summary>
public class InGameMenuTests
{
    const MenuOptions AllOptions = MenuOptions.Valid | MenuOptions.ShareStars | MenuOptions.SyncEnemies | MenuOptions.SyncSwitches;

    static int FreeUdpPort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }

    static (FakeGameMemory mem, InGameLauncher launcher, LauncherSettingsStore store) Start(int port)
    {
        var mem = new FakeGameMemory();
        mem.InstallMailbox(0x1000);
        var store = new LauncherSettingsStore(Path.Combine(Path.GetTempPath(), $"gcoop-menu-{Guid.NewGuid():N}.json"));
        store.Update(store.Current with { Name = "Fahd", Character = "luigi", Port = port, LaunchDolphin = false, SyncEnemies = false });
        return (mem, new InGameLauncher(store, new GameLink(() => mem)), store);
    }

    static async Task StepUntil(InGameLauncher launcher, Func<bool> done, int timeoutMs = 5000)
    {
        var clock = Stopwatch.StartNew();
        while (!done())
        {
            if (clock.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("the menu never reached the expected state");
            await launcher.StepAsync(CancellationToken.None);
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task PublishesPreferencesBeforeAnyRequest()
    {
        var (mem, launcher, _) = Start(FreeUdpPort());
        await using (launcher)
        {
            await StepUntil(launcher, () => mem.GameReadMenuStatus().seq > 0);
            var (seq, _, status) = mem.GameReadMenuStatus();
            Assert.Equal(0u, seq & 1);
            Assert.Equal(MenuState.Idle, status.State);
            Assert.Equal(2u, status.PrefCharacter);
            Assert.Equal(MenuOptions.Valid | MenuOptions.ShareStars | MenuOptions.SyncSwitches, status.PrefOptions);
            Assert.Contains("Fahd", status.Lines[0]);
        }
    }

    [Fact]
    public async Task HostsAndLeavesFromTheMenu()
    {
        var (mem, launcher, store) = Start(FreeUdpPort());
        await using (launcher)
        {
            await StepUntil(launcher, () => mem.GameReadMenuStatus().seq > 0);

            mem.GameMenuRequest(new MenuRequest { Serial = 1, Command = MenuCommand.HostPrivate, Character = 1, Options = AllOptions });
            await StepUntil(launcher, () => mem.GameReadMenuStatus() is { handled: 1, status.State: MenuState.Hosting });
            var hosting = mem.GameReadMenuStatus().status;
            Assert.True(hosting.IsHost);
            Assert.StartsWith("Invite", hosting.Lines[1]);
            var me = Assert.Single(hosting.Players);
            Assert.Equal("Fahd", me.Name);
            Assert.True(me.IsHost && me.IsYou);
            Assert.Equal("mario", store.Current.Character);
            Assert.True(store.Current.SyncEnemies);

            mem.GameMenuRequest(new MenuRequest { Serial = 2, Command = MenuCommand.JoinAddress, Text = "127.0.0.1:1", Character = 1, Options = AllOptions });
            await StepUntil(launcher, () => mem.GameReadMenuStatus().handled == 2);
            await StepUntil(launcher, () => mem.GameReadMenuStatus().status.Lines[3].Length > 0);
            Assert.Equal(MenuState.Hosting, mem.GameReadMenuStatus().status.State);

            mem.GameMenuRequest(new MenuRequest { Serial = 3, Command = MenuCommand.Leave, Character = 1, Options = AllOptions });
            await StepUntil(launcher, () => mem.GameReadMenuStatus() is { handled: 3, status.State: MenuState.Idle });
            Assert.Empty(mem.GameReadMenuStatus().status.Players);
        }
    }

    [Fact]
    public async Task IgnoresRequestsMadeBeforeItAttached()
    {
        var (mem, launcher, _) = Start(FreeUdpPort());
        mem.GameMenuRequest(new MenuRequest { Serial = 7, Command = MenuCommand.HostPublic, Options = AllOptions });
        await using (launcher)
        {
            await StepUntil(launcher, () => mem.GameReadMenuStatus().seq > 0);
            for (int i = 0; i < 5; i++) await launcher.StepAsync(CancellationToken.None);
            var (_, handled, status) = mem.GameReadMenuStatus();
            Assert.Equal(7u, handled);
            Assert.Equal(MenuState.Idle, status.State);
        }
    }
}
