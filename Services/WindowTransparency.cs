using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TaskDesk.Services;

// Whole-window alpha keeps native resize/caption behavior and needs no rendering library.
internal sealed class WindowTransparency : IDisposable
{
    private const int ExtendedStyle=-20,Layered=0x80000;
    private readonly HwndSource source;
    private int percent=100;
    public WindowTransparency(Window window)
    {
        source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)
            ??throw new InvalidOperationException("The window handle is not ready.");
        source.AddHook(KeepLayeredStyle);
    }
    private nint KeepLayeredStyle(nint hwnd,int message,nint wParam,nint lParam,ref bool handled)
    {
        // WPF normally strips WS_EX_LAYERED from an opaque rendering target during
        // WM_STYLECHANGING. Retain only our constant-alpha flag while it is needed.
        if(message==0x007C&&(int)wParam==ExtendedStyle&&percent<100)
        {
            Marshal.WriteInt32(lParam,4,Marshal.ReadInt32(lParam,4)|Layered);
            handled=true;
        }
        return 0;
    }
    public void Apply(int value)
    {
        var previous=percent;percent=value;
        try
        {
            var handle=source.Handle;var style=GetWindowLong(handle,ExtendedStyle);
            var target=percent==100?style&~Layered:style|Layered;
            if(target!=style&&SetWindowLong(handle,ExtendedStyle,target)==0&&Marshal.GetLastPInvokeError()!=0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            if(percent<100&&!SetLayeredWindowAttributes(handle,0,(byte)Math.Round(255*percent/100.0),2))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            if(percent==100&&target!=style)RedrawWindow(handle,0,0,0x485);
        }
        catch{percent=previous;throw;}
    }
    public void Dispose(){if(!source.IsDisposed)source.RemoveHook(KeepLayeredStyle);}
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] private static extern int GetWindowLong(nint hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongW",SetLastError=true)] private static extern int SetWindowLong(nint hwnd,int index,int value);
    [DllImport("user32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(nint hwnd,uint color,byte alpha,uint flags);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool RedrawWindow(nint hwnd,nint rect,nint region,uint flags);
}
