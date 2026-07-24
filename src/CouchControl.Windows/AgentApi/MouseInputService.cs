using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CouchControl.Windows.AgentApi;

public interface IMouseInputService
{
    void Move(int deltaX, int deltaY);

    void Scroll(int delta);

    void Button(MouseButton button, bool pressed);
}

public enum MouseButton
{
    Left,
    Right
}

internal sealed class WindowsMouseInputService : IMouseInputService
{
    private const uint InputMouse = 0;
    private const uint MouseEventMove = 0x0001;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint MouseEventWheel = 0x0800;

    public void Move(int deltaX, int deltaY) =>
        Send(new MouseInput(deltaX, deltaY, 0, MouseEventMove, 0, IntPtr.Zero));

    public void Scroll(int delta) =>
        Send(new MouseInput(0, 0, unchecked((uint)delta), MouseEventWheel, 0, IntPtr.Zero));

    public void Button(MouseButton button, bool pressed)
    {
        uint flags = (button, pressed) switch
        {
            (MouseButton.Left, true) => MouseEventLeftDown,
            (MouseButton.Left, false) => MouseEventLeftUp,
            (MouseButton.Right, true) => MouseEventRightDown,
            _ => MouseEventRightUp
        };

        Send(new MouseInput(0, 0, 0, flags, 0, IntPtr.Zero));
    }

    private static void Send(MouseInput mouseInput)
    {
        var input = new Input
        {
            Type = InputMouse,
            Data = new InputUnion { Mouse = mouseInput }
        };

        if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows rejected the remote mouse input.");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct MouseInput(
        int deltaX,
        int deltaY,
        uint mouseData,
        uint flags,
        uint time,
        IntPtr extraInfo)
    {
        public readonly int DeltaX = deltaX;
        public readonly int DeltaY = deltaY;
        public readonly uint MouseData = mouseData;
        public readonly uint Flags = flags;
        public readonly uint Time = time;
        public readonly IntPtr ExtraInfo = extraInfo;
    }
}
