using System.Runtime.InteropServices;

namespace LiveCaptionsTranslator.apis
{
    public static class WindowsAPI
    {
        public const int GWL_EXSTYLE = -20;

        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public const uint LWA_ALPHA = 0x00000002;

        public static readonly nint HWND_TOPMOST = -1;
        public static readonly nint HWND_NOTOPMOST = -2;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_FRAMECHANGED = 0x0020;

        public const int SW_MINIMIZE = 6;
        public const int SW_RESTORE = 9;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ShowWindow(nint hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(nint hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowThreadProcessId(nint hWnd, out int lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetForegroundWindow(nint hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool MoveWindow(
            nint hWnd,
            int X,
            int Y,
            int nWidth,
            int nHeight,
            bool bRepaint
        );

        public const uint SPI_GETWORKAREA = 0x0030;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, out RECT pvParam, uint fWinIni);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(nint hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetLayeredWindowAttributes(nint hWnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowRect(nint hWnd, out RECT lpRect);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
