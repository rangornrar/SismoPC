using SismoPC.Core.Model;

namespace SismoPC.Core.Tests;

public class PcAgeTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 19, 0, 0, DateTimeKind.Unspecified);

    [Fact]
    public void Indice_le_plus_ancien_parmi_les_dates_en_service()
    {
        DateClue[] clues =
        [
            new("Windows installé", new DateTime(2024, 10, 2, 0, 0, 0, DateTimeKind.Unspecified), DateClueKind.InUseSince, ""),
            new("SSD Samsung 980", PcAge.FromPowerOnHours(14_000, Now), DateClueKind.InUseSince, ""),
            new("Écran", new DateTime(2015, 3, 2, 0, 0, 0, DateTimeKind.Unspecified), DateClueKind.Manufactured, ""),
            new("BIOS", new DateTime(2023, 1, 5, 0, 0, 0, DateTimeKind.Unspecified), DateClueKind.Firmware, ""),
        ];

        // 14 000 h = 583 jours avant : avril 2025, mais Windows (octobre 2024) est plus ancien.
        PcAge.InUseSince(clues, Now).ShouldBe(new DateTime(2024, 10, 2, 0, 0, 0, DateTimeKind.Unspecified));
        PcAge.Summary(clues, Now).ShouldBe("En service depuis au moins octobre 2024 (environ 2 ans)");
    }

    [Fact]
    public void L_ecran_ne_date_pas_le_PC()
    {
        DateClue[] clues = [new("Écran", new DateTime(2015, 3, 2, 0, 0, 0, DateTimeKind.Unspecified), DateClueKind.Manufactured, "")];
        PcAge.InUseSince(clues, Now).ShouldBeNull();
        PcAge.Summary(clues, Now).ShouldBe("Date inconnue");
    }

    [Theory]
    [InlineData(2026, 11, 1, "moins d'un mois")]
    [InlineData(2026, 3, 17, "environ 8 mois")]
    [InlineData(2025, 9, 1, "environ 1 an")]
    [InlineData(2021, 6, 1, "environ 5 ans")]
    public void Age_lisible(int y, int m, int d, string expected) =>
        PcAge.AgeText(new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Unspecified), Now).ShouldBe(expected);

    [Fact]
    public void Identifiant_PCI_et_fabricant_de_la_carte()
    {
        var id = HardwareNames.ParsePciId(@"PCI\VEN_10DE&DEV_2484&SUBSYS_39883842&REV_A1\4&1D8D3F9E&0&0008").ShouldNotBeNull();
        id.Vendor.ShouldBe(0x10DEu);
        id.Device.ShouldBe(0x2484u);
        id.SubsystemVendor.ShouldBe(0x3842u);
        HardwareNames.PciVendor(id.Vendor).ShouldBe("NVIDIA");
        HardwareNames.PciVendor(id.SubsystemVendor).ShouldBe("EVGA");
        HardwareNames.ParsePciId("ROOT\\BasicDisplay\\0000").ShouldBeNull();
    }

    [Fact]
    public void Marque_de_disque_et_semaine_d_ecran()
    {
        HardwareNames.DiskBrand("Samsung SSD 980 1TB").ShouldBe("Samsung");
        HardwareNames.DiskBrand("WDC WD10EZEX-08WN4A0").ShouldBe("Western Digital");
        HardwareNames.DiskBrand("ST2000DM008-2FR102").ShouldBe("Seagate");
        HardwareNames.DiskBrand("Inconnu 123").ShouldBeNull();
        HardwareNames.WeekToDate(2023, 12).ShouldBe(new DateTime(2023, 3, 20, 0, 0, 0, DateTimeKind.Unspecified));
        HardwareNames.WeekToDate(1, 12).ShouldBeNull();
    }
}
