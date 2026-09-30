// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;
using System.Runtime.InteropServices;

namespace ScreenRecorder.Platform.Windows.Capture;

public sealed record DxgiOutputInfo(long AdapterLuid, int OutputIndex, string DeviceName,
    CaptureRegion Bounds, bool Attached, bool IdentityRotation, bool IsDefaultAdapter);

public interface IDxgiOutputCatalog
{
    IReadOnlyList<DxgiOutputInfo> GetOutputs();
}

public sealed class DxgiOutputCatalog : IDxgiOutputCatalog
{
    public IReadOnlyList<DxgiOutputInfo> GetOutputs()
    {
        if (!OperatingSystem.IsWindows()) return [];
        nint device = 0, context = 0, dxgiDevice = 0, adapter = 0;
        try
        {
            Marshal.ThrowExceptionForHR(D3D11CreateDevice(0, 1, 0, 0, 0, 0, 7, out device, out _, out context));
            var iid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c"); // IDXGIDevice
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device, ref iid, out dxgiDevice));
            Marshal.ThrowExceptionForHR(Method<GetAdapter>(dxgiDevice, 7)(dxgiDevice, out adapter));
            Marshal.ThrowExceptionForHR(Method<GetAdapterDesc>(adapter, 8)(adapter, out var adapterDesc));
            var result = new List<DxgiOutputInfo>();
            for (uint index = 0; index < 64; index++)
            {
                var hr = Method<EnumOutputs>(adapter, 7)(adapter, index, out var output);
                if (hr == unchecked((int)0x887A0002)) break; // DXGI_ERROR_NOT_FOUND
                try
                {
                    Marshal.ThrowExceptionForHR(hr);
                    Marshal.ThrowExceptionForHR(Method<GetOutputDesc>(output, 7)(output, out var desc));
                    result.Add(new(adapterDesc.Luid, checked((int)index), desc.DeviceName,
                        new(desc.Left, desc.Top, checked(desc.Right - desc.Left), checked(desc.Bottom - desc.Top)),
                        desc.Attached != 0, desc.Rotation == 1, true));
                }
                finally { Release(output); }
            }
            return result;
        }
        finally
        {
            Release(adapter); Release(dxgiDevice); Release(context); Release(device);
        }
    }

    private static T Method<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));
    private static void Release(nint instance) { if (instance != 0) Marshal.Release(instance); }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(nint adapter, uint driverType, nint software, uint flags,
        nint levels, uint levelCount, uint sdkVersion, out nint device, out uint level, out nint context);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetAdapter(nint self, out nint adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumOutputs(nint self, uint index, out nint output);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetAdapterDesc(nint self, out AdapterDesc desc);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetOutputDesc(nint self, out OutputDesc desc);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDesc
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public long Luid;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OutputDesc
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public int Left, Top, Right, Bottom, Attached;
        public uint Rotation;
        public nint Monitor;
    }
}
