using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace StreamingHost;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // WPF only calls OleInitialize on the UI thread, but Windows.Graphics.Capture
        // (and any RoGetActivationFactory call) needs the apartment to be initialized
        // for WinRT — otherwise factories report E_NOINTERFACE. Single-threaded apartment
        // matches WPF's STA. Failure is non-fatal: it usually means RoInitialize was
        // already called, in which case we're fine.
        try
        {
            var hr = RoInitialize(RO_INIT_SINGLETHREADED);
            // S_OK (0), S_FALSE (1), RPC_E_CHANGED_MODE (0x80010106) all mean usable apartment
            if (hr < 0 && hr != unchecked((int)0x80010106))
                System.Diagnostics.Debug.WriteLine($"RoInitialize failed: 0x{hr:X8}");
        }
        catch (DllNotFoundException) { /* extremely old Windows; capture will fail later anyway */ }

        base.OnStartup(e);
    }

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoInitialize(int initType);
    private const int RO_INIT_SINGLETHREADED = 0;
}
