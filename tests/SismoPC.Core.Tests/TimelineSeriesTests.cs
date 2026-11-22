using SismoPC.Core.Model;

namespace SismoPC.Core.Tests;

public class TimelineSeriesTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(5)]
    [InlineData(60)]
    [InlineData(3600)]
    public void Interruption_revient_a_zero_sans_relier_les_mesures(int resolution)
    {
        var step = TimeSpan.FromSeconds(resolution);
        var resume = Start + step * 10;
        var values = TimelineSeries.WithZeroGaps([
            new(Start, 80, step), new(resume, 60, step)
        ], Start, resume + step);
        values.ShouldContain(p => p.At == Start + step && p.Value == 0);
        values.ShouldContain(p => p.At == resume && p.Value == 0);
        values.ShouldContain(p => p.At == resume && p.Value == 60);
    }

    [Fact]
    public void Mesures_contigues_ne_sont_pas_interrompues_et_source_reste_intacte()
    {
        var source = new[] { new TimedValue(Start, 80, TimeSpan.FromMinutes(1)), new TimedValue(Start.AddMinutes(1), 60, TimeSpan.FromMinutes(1)) };
        var values = TimelineSeries.WithZeroGaps(source, Start, Start.AddMinutes(2));
        values.ShouldNotContain(p => p.Value == 0 && p.At > Start && p.At < Start.AddMinutes(2));
        source.Length.ShouldBe(2);
        source[0].Value.ShouldBe(80);
    }

    [Fact]
    public void Periode_vide_est_plate_a_zero()
    {
        var values = TimelineSeries.WithZeroGaps([], Start, Start.AddHours(1));
        values.Count.ShouldBe(2);
        values.All(p => p.Value == 0).ShouldBeTrue();
        values[1].At.ShouldBe(Start.AddHours(1));
    }
}
