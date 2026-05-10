using System;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.Direct3D11;
using WinRT;
using D3D11Device = Vortice.Direct3D11.ID3D11Device;
using IDirect3DDevice = Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice;
using IDirect3DSurface = Windows.Graphics.DirectX.Direct3D11.IDirect3DSurface;

namespace StreamingHost.Capture;

/// <summary>
/// Bridges Vortice.Direct3D11.ID3D11Device (and its textures) to the WinRT
/// Windows.Graphics.DirectX.Direct3D11 surface types that
/// Windows.Graphics.Capture works with.
///
/// Two pieces of plumbing:
///  1. <see cref="CreateDirect3DDevice"/> wraps a D3D11 device into an
///     IDirect3DDevice via the d3d11.dll export
///     <c>CreateDirect3D11DeviceFromDXGIDevice</c>.
///  2. <see cref="GetTexture2D"/> unwraps an IDirect3DSurface back to an
///     ID3D11Texture2D via IDirect3DDxgiInterfaceAccess::GetInterface.
/// </summary>
public static class Direct3D11Interop
{
    private static readonly Guid IID_ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    /// <summary>Wraps a D3D11 device for use with Windows.Graphics.Capture APIs.</summary>
    public static IDirect3DDevice CreateDirect3DDevice(D3D11Device device)
    {
        using var dxgiDevice = device.QueryInterface<Vortice.DXGI.IDXGIDevice>();
        var hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out IntPtr inspectablePtr);
        if (hr != 0) Marshal.ThrowExceptionForHR(hr);
        try
        {
            return MarshalInterface<IDirect3DDevice>.FromAbi(inspectablePtr);
        }
        finally
        {
            Marshal.Release(inspectablePtr);
        }
    }

    /// <summary>Unwraps an IDirect3DSurface back to an ID3D11Texture2D.</summary>
    public static ID3D11Texture2D GetTexture2D(IDirect3DSurface surface)
    {
        var access = WinRT.CastExtensions.As<IDirect3DDxgiInterfaceAccess>(surface);
        var iid = IID_ID3D11Texture2D;
        access.GetInterface(ref iid, out IntPtr texPtr).CheckError();
        try
        {
            // Vortice's ID3D11Texture2D has a ctor that wraps a native COM pointer.
            return new ID3D11Texture2D(texPtr);
        }
        catch
        {
            Marshal.Release(texPtr);
            throw;
        }
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(
        IntPtr dxgiDevice, out IntPtr graphicsDevice);

    /// <summary>COM interop interface implemented by every WinRT IDirect3DSurface / IDirect3DDevice.</summary>
    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        Result GetInterface([In] ref Guid iid, out IntPtr ppv);
    }
}
