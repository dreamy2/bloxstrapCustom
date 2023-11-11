using System;
using System.Windows;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;

namespace Bloxstrap.Integrations
{
    public class WindowController : IDisposable
    {
        private readonly ActivityWatcher _activityWatcher;
        private IntPtr _currentWindow;
        private bool _foundWindow = false;

        public WindowController(ActivityWatcher activityWatcher)
        {
            _activityWatcher = activityWatcher;
            _activityWatcher.OnRPCMessage += (_, message) => OnMessage(message);

            _currentWindow = FindWindow("Roblox",0);
            _foundWindow = !(_currentWindow == (IntPtr)0);
        }

        public const uint WM_SETTEXT = 0x000C;

        public void OnMessage(Message message) {
            const string LOG_IDENT = "WindowController::OnMessage";

            if (!_foundWindow) {
                _currentWindow = FindWindow("Roblox",0);
                _foundWindow = !(_currentWindow == (IntPtr)0);
            }

            if (_currentWindow == (IntPtr)0) {return;}

            if (message.Command == "SetWindow")
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

                int x = 0; 
                int y = 0; 

                // Target 720p as default
                int w = 1280; 
                int h = 720;

                double originalW = SystemParameters.PrimaryScreenWidth; 
                double originalH = SystemParameters.PrimaryScreenHeight;

                if (windowData.X is not null) {
                    x = (int) windowData.X;
                }

                if (windowData.Y is not null) {
                    y = (int) windowData.Y;
                }

                if (windowData.Width is not null) {
                    w = (int) windowData.Width;
                }

                if (windowData.Height is not null) {
                    h = (int) windowData.Height;
                }

                // This method for scaling is horrible, but it works.
                
                if (windowData.ScaleWidth is not null) {
                    float scale = (float) (originalW / windowData.ScaleWidth);

                    w = (int) Math.Round(w * scale);
                    x = (int) Math.Round(x * scale);
                }

                if (windowData.ScaleHeight is not null) {
                    float scale = (float) (originalH / windowData.ScaleHeight);

                    h = (int) Math.Round(h * scale);
                    y = (int) Math.Round(y * scale);
                }

                MoveWindow(_currentWindow,x,y,w,h,true);
                App.Logger.WriteLine(LOG_IDENT, $"Updated Window Properties");
            }
            else if (message.Command == "SetWindowTitle")
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
    }
}
