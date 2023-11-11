using System.Windows;
using System.Runtime.InteropServices;
using System.Drawing;

public struct Rect {
   public int Left { get; set; }
   public int Top { get; set; }
   public int Right { get; set; }
   public int Bottom { get; set; }
}

namespace Bloxstrap.Integrations
{
    public class WindowController : IDisposable
    {
        private readonly ActivityWatcher _activityWatcher;
        private IntPtr _currentWindow;
        private bool _foundWindow = false;
        public const uint WM_SETTEXT = 0x000C;

        private double screenSizeX = 0;
        private double screenSizeY = 0;
        private int _lastX = 0;
        private int _lastY = 0;
        private int _lastWidth = 0;
        private int _lastHeight = 0;

        // 1280x720 as default
        private double defaultScreenSizeX = 1280;
        private double defaultScreenSizeY = 720;


        public WindowController(ActivityWatcher activityWatcher)
        {
            _activityWatcher = activityWatcher;
            _activityWatcher.OnRPCMessage += (_, message) => OnMessage(message);

            _currentWindow = FindWindow("Roblox",0);
            _foundWindow = !(_currentWindow == (IntPtr)0);

            if (_foundWindow) { onWindowFound(); }

            screenSizeX = SystemParameters.PrimaryScreenWidth;
            screenSizeY = SystemParameters.PrimaryScreenHeight;
        }

        public void onWindowFound() {
            Rect winRect = new Rect();
            GetWindowRect(_currentWindow, ref winRect);    
            _lastX = winRect.Left;
            _lastY = winRect.Top;
            _lastWidth = winRect.Right - winRect.Left;
            _lastHeight = winRect.Bottom - winRect.Top;

            //dpi awareness
            using (Graphics graphics = Graphics.FromHwnd(_currentWindow))
                {
                    screenSizeX *= (double)(graphics.DpiX / 96);
                    screenSizeY *= (double)(graphics.DpiY / 96);
                }

            App.Logger.WriteLine("WindowController::onWindowFound", $"WinSize X:{_lastX} Y:{_lastY} W:{_lastWidth} H:{_lastHeight} sW:{screenSizeX} sH:{screenSizeY}");
        }

        public void OnMessage(Message message) {
            const string LOG_IDENT = "WindowController::OnMessage";

            // try to find window now
            if (!_foundWindow) {
                _currentWindow = FindWindow("Roblox",0);
                _foundWindow = !(_currentWindow == (IntPtr)0);

                if (_foundWindow) { onWindowFound(); }
            }

            if (_currentWindow == (IntPtr)0) {return;}

            if (message.Command == "SetWindow" && App.Settings.Prop.CanGameMoveWindow)
            {
                Models.BloxstrapRPC.WindowMessage? windowData;

                try
                {
                    windowData = message.Data.Deserialize<Models.BloxstrapRPC.WindowMessage>();
                }
                catch (Exception)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Failed to parse message! (JSON deserialization threw an exception)");
                    return;
                }

                if (windowData is null)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Failed to parse message! (JSON deserialization returned null)");
                    return;
                }

                double scaleWidth = defaultScreenSizeX;
                double scaleHeight = defaultScreenSizeY;

                if (windowData.ScaleWidth is not null) {
                    scaleWidth = (double) windowData.ScaleWidth;
                }

                if (windowData.ScaleHeight is not null) {
                    scaleHeight = (double) windowData.ScaleHeight;
                }

                float scaleX = (float) (screenSizeX / scaleWidth);
                float scaleY = (float) (screenSizeY / scaleHeight);

                if (windowData.X is not null) {
                    _lastX = (int) (windowData.X * scaleX);
                }

                if (windowData.Y is not null) {
                    _lastY = (int) (windowData.Y * scaleY);
                }

                if (windowData.Width is not null) {
                    _lastWidth = (int) (windowData.Width * scaleX);
                }

                if (windowData.Height is not null) {
                    _lastHeight = (int) (windowData.Height * scaleY);
                }

                MoveWindow(_currentWindow,_lastX,_lastY,_lastWidth,_lastHeight,true);
                App.Logger.WriteLine(LOG_IDENT, $"Updated Window Properties");
            }
            else if (message.Command == "SetWindowTitle"  && App.Settings.Prop.CanGameMoveWindow)
            {
                Models.BloxstrapRPC.WindowTitle? windowData;

                try
                {
                    windowData = message.Data.Deserialize<Models.BloxstrapRPC.WindowTitle>();
                }
                catch (Exception)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Failed to parse message! (JSON deserialization threw an exception)");
                    return;
                }

                if (windowData is null)
                {
                    App.Logger.WriteLine(LOG_IDENT, "Failed to parse message! (JSON deserialization returned null)");
                    return;
                }

                string title = "Roblox";
                if (windowData.Name is not null) {
                    title = windowData.Name;
                }

                SendMessage(_currentWindow, WM_SETTEXT, IntPtr.Zero, title);
            }
            else
            {
                return;
            }
        }
        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }

        private IntPtr FindWindow(string title, int index)
        {
            List<Process> l = new List<Process>();

            Process[] tempProcesses;
            tempProcesses = Process.GetProcesses();
            foreach (Process proc in tempProcesses)
            {
                if (proc.MainWindowTitle == title)
                {
                    l.Add(proc);
                }
            }

            if (l.Count > index) return l[index].MainWindowHandle;
            return (IntPtr)0;
        }

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hwnd, ref Rect rectangle);
    }
}
