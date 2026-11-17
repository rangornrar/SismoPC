using SismoPC.Core.Health;
using SismoPC.Core.Ipc;

namespace SismoPC.Core.Tests;

public class IpcTests
{
    [Fact]
    public async Task Chaque_abonne_recoit_les_evenements()
    {
        var hub = new EventHub();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var e1 = hub.Subscribe(cts.Token).GetAsyncEnumerator(cts.Token);
        var e2 = hub.Subscribe(cts.Token).GetAsyncEnumerator(cts.Token);

        // L'abonnement est enregistré au premier MoveNextAsync : on le lance avant de publier.
        var next1 = e1.MoveNextAsync();
        var next2 = e2.MoveNextAsync();
        await WaitUntil(() => hub.SubscriberCount == 2);

        hub.Publish(new PipeEvent("alert", "warning", "Espace disque faible sur D:", "8 % libres"));

        (await next1).ShouldBeTrue();
        (await next2).ShouldBeTrue();
        e1.Current.Title.ShouldBe("Espace disque faible sur D:");
        e2.Current.Kind.ShouldBe("alert");

        await e1.DisposeAsync();
        await e2.DisposeAsync();
        hub.SubscriberCount.ShouldBe(0);
    }

    [Fact]
    public void Protocole_aller_retour_json()
    {
        var status = new LiveStatus(
            new DateTimeOffset(2026, 11, 17, 14, 0, 0, TimeSpan.Zero),
            new Dictionary<string, double> { ["CpuPercent"] = 6 },
            87, "Bon état", [new Penalty("D: : 8 % libres", 10)], false,
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), true);

        var line = PipeProtocol.Serialize(status);
        var back = PipeProtocol.Deserialize<LiveStatus>(line).ShouldNotBeNull();

        line.ShouldNotContain("\n");
        back.Score.ShouldBe(87);
        back.Penalties.ShouldHaveSingleItem().Points.ShouldBe(10);
        back.Metrics["CpuPercent"].ShouldBe(6);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(20);
        }

        condition().ShouldBeTrue();
    }
}
