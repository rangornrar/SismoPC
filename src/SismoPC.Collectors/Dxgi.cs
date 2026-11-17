using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SismoPC.Collectors;

/// <summary>Carte graphique vue par DirectX : nom, identifiants PCI et mémoire vidéo dédiée.</summary>
public sealed record DxgiAdapter(string Name, uint VendorId, uint DeviceId, uint SubSystemId, ulong DedicatedVideoMemory);

/// <summary>
/// Énumère les cartes graphiques par DXGI (IDXGIFactory1::EnumAdapters1, IDXGIAdapter1::GetDesc1).
/// Source la plus fiable de la mémoire vidéo : WMI (AdapterRAM) plafonne à 4 Go.
/// </summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class Dxgi
{
    private const uint SoftwareAdapterFlag = 2;

    [LibraryImport("dxgi.dll")]
    private static partial int CreateDXGIFactory1(in Guid riid, out IntPtr factory);

    public static IReadOnlyList<DxgiAdapter> Adapters()
    {
        var list = new List<DxgiAdapter>();
        var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); // IID_IDXGIFactory1
        IntPtr factory;
        try
        {
            if (CreateDXGIFactory1(in iid, out factory) < 0 || factory == IntPtr.Zero)
            {
                return list;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return list;
        }

        try
        {
            // Table virtuelle : IUnknown (3) + IDXGIObject (4) + IDXGIFactory (5) → EnumAdapters1 à l'index 12.
            var enumAdapters1 = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)(*(IntPtr**)factory)[12];
            for (uint i = 0; i < 16; i++)
            {
                IntPtr adapter;
                if (enumAdapters1(factory, i, &adapter) < 0)
                {
                    break; // DXGI_ERROR_NOT_FOUND : plus de carte.
                }

                try
                {
                    // IUnknown (3) + IDXGIObject (4) + IDXGIAdapter (3) → GetDesc1 à l'index 10.
                    var getDesc1 = (delegate* unmanaged[Stdcall]<IntPtr, AdapterDesc1*, int>)(*(IntPtr**)adapter)[10];
                    AdapterDesc1 desc;
                    if (getDesc1(adapter, &desc) >= 0 && (desc.Flags & SoftwareAdapterFlag) == 0)
                    {
                        list.Add(new DxgiAdapter(new string(desc.Description), desc.VendorId, desc.DeviceId, desc.SubSysId, desc.DedicatedVideoMemory));
                    }
                }
                finally
                {
                    Release(adapter);
                }
            }
        }
        finally
        {
            Release(factory);
        }

        return list;
    }

    private static void Release(IntPtr unknown)
    {
        var release = (delegate* unmanaged[Stdcall]<IntPtr, uint>)(*(IntPtr**)unknown)[2];
        release(unknown);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdapterDesc1
    {
        public fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint LuidLowPart;
        public int LuidHighPart;
        public uint Flags;
    }
}
