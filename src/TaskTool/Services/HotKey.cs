using System.Windows.Interop;

namespace TaskTool.Services;

/// <summary>Atalho global do Windows (RegisterHotKey) usando uma janela de mensagens invisível.</summary>
public sealed class HotKey : IDisposable
{
    private const int Id = 0xB001;
    private readonly HwndSource _source;
    private readonly Action _onPressed;

    public bool Registrado { get; }
    public string Descricao { get; }

    public HotKey(uint modificadores, uint teclaVirtual, string descricao, Action onPressed)
    {
        _onPressed = onPressed;
        Descricao = descricao;
        _source = new HwndSource(new HwndSourceParameters("TaskToolHotKey")
        {
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
            WindowStyle = 0,
        });
        _source.AddHook(WndProc);
        Registrado = Native.RegisterHotKey(_source.Handle, Id, modificadores | Native.MOD_NOREPEAT, teclaVirtual);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && wParam.ToInt32() == Id)
        {
            _onPressed();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (Registrado) Native.UnregisterHotKey(_source.Handle, Id);
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
