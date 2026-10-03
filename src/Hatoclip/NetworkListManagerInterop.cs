using System.Runtime.InteropServices;

namespace Hatoclip;

/// <summary>
/// Minimal hand-written COM interop for the Windows Network List Manager (netlistmgr.h), used only to
/// read the network category (Public / Private / Domain). The interfaces are dual (IDispatch-based);
/// methods are declared in vtable order and only the leading methods we call are declared where the
/// remaining ones are not needed.
/// </summary>
internal static class NetworkListManagerInterop
{
    public static readonly Guid ClsidNetworkListManager = new("DCB00C01-570F-4A9B-8D69-199FDBA5723B");

    public enum NlmNetworkCategory
    {
        Public = 0,
        Private = 1,
        DomainAuthenticated = 2,
    }

    public enum NlmEnumNetwork
    {
        Connected = 0x01,
        Disconnected = 0x02,
        All = 0x03,
    }

    [ComImport]
    [Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    public interface INetworkListManager
    {
        IEnumNetworks GetNetworks(NlmEnumNetwork flags);

        INetwork GetNetwork(Guid networkId);

        IEnumNetworkConnections GetNetworkConnections();
    }

    [ComImport]
    [Guid("DCB00002-570F-4A9B-8D69-199FDBA5723B")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    public interface INetwork
    {
        [return: MarshalAs(UnmanagedType.BStr)]
        string GetName();

        void SetName([MarshalAs(UnmanagedType.BStr)] string name);

        [return: MarshalAs(UnmanagedType.BStr)]
        string GetDescription();

        void SetDescription([MarshalAs(UnmanagedType.BStr)] string description);

        Guid GetNetworkId();

        int GetDomainType();

        IEnumNetworkConnections GetNetworkConnections();

        void GetTimeCreatedAndConnected(out uint lowCreated, out uint highCreated, out uint lowConnected, out uint highConnected);

        short IsConnectedToInternet { get; }

        short IsConnected { get; }

        int GetConnectivity();

        NlmNetworkCategory GetCategory();
    }

    [ComImport]
    [Guid("DCB00003-570F-4A9B-8D69-199FDBA5723B")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    public interface IEnumNetworks
    {
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object GetNewEnum();

        [PreserveSig]
        int Next(int celt, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] INetwork[] elements, out int fetched);
    }

    [ComImport]
    [Guid("DCB00005-570F-4A9B-8D69-199FDBA5723B")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    public interface INetworkConnection
    {
        INetwork GetNetwork();

        short IsConnectedToInternet { get; }

        short IsConnected { get; }

        int GetConnectivity();

        Guid GetConnectionId();

        Guid GetAdapterId();
    }

    [ComImport]
    [Guid("DCB00006-570F-4A9B-8D69-199FDBA5723B")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    public interface IEnumNetworkConnections
    {
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object GetNewEnum();

        [PreserveSig]
        int Next(int celt, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] INetworkConnection[] elements, out int fetched);
    }

    public static INetworkListManager Create()
    {
        var type = Type.GetTypeFromCLSID(ClsidNetworkListManager, throwOnError: true)!;
        return (INetworkListManager)Activator.CreateInstance(type)!;
    }
}
