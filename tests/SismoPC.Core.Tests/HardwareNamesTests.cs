using SismoPC.Core.Model;

namespace SismoPC.Core.Tests;

public class HardwareNamesTests
{
    [Theory]
    [InlineData(26L, "DDR4")]
    [InlineData(34L, "DDR5")]
    [InlineData(0L, null)]
    public void Type_de_memoire(long code, string? expected) => HardwareNames.MemoryType(code).ShouldBe(expected);

    [Fact]
    public void Bus_NVMe_et_SATA()
    {
        HardwareNames.DiskBus(17).ShouldBe("NVMe");
        HardwareNames.DiskBus(11).ShouldBe("SATA");
        HardwareNames.DiskBus(null).ShouldBeNull();
    }

    [Fact]
    public void Ecran_27_pouces()
    {
        // Dalle 60 × 34 cm : environ 27 pouces.
        HardwareNames.DiagonalInches(60, 34)!.Value.ShouldBe(27.1, tolerance: 0.2);
        HardwareNames.DiagonalInches(0, 34).ShouldBeNull();
        HardwareNames.MonitorBrand("GSM").ShouldBe("LG");
        HardwareNames.MonitorBrand("XYZ").ShouldBe("XYZ");
    }

    [Fact]
    public void Texte_EDID_jusqu_au_zero()
    {
        ushort[] name = ['D', 'E', 'L', 'L', ' ', 'U', '2', '7', '2', '3', 0, 0];
        HardwareNames.EdidText(name).ShouldBe("DELL U2723");
    }

    [Fact]
    public void Tailles_lisibles()
    {
        HardwareNames.Bytes(8L * 1024 * 1024 * 1024).ShouldBe("8 Go");
        HardwareNames.Bytes(12884901888).ShouldBe("12 Go");
        HardwareNames.DiskSize(1_000_204_886_016).ShouldBe("1 To");
        HardwareNames.DiskSize(512_110_190_592).ShouldBe("512 Go");
        HardwareNames.LinkSpeed(1e9).ShouldBe("1 Gbit/s");
        HardwareNames.LinkSpeed(2.5e9).ShouldBe("2,5 Gbit/s");
    }
    [Fact]
    public void Disques_identiques_sont_distincts_et_numero_de_serie_prioritaire()
    {
        DiskIdentity.Label("SSD", "id1", "serie1", "0").ShouldNotBe(DiskIdentity.Label("SSD", "id2", "serie2", "1"));
        DiskIdentity.Label("SSD", "id1", "serie1", "0").ShouldBe(DiskIdentity.Label("SSD", "id2", "serie1", "2"));
    }
}
