using System;
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
        public void OnMessage(Message message) {
            const string LOG_IDENT = "WindowController::OnMessage";

            if (message.Command != "SetWindow")
                return;

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

            if (!_foundWindow) {
                _currentWindow = FindWindow("Roblox",0);
                _foundWindow = !(_currentWindow == (IntPtr)0);
            }

            if (_currentWindow == (IntPtr)0) {return;}

            int x = 100; 
            int y = 100; 
            int w = 100; 
            int h = 100;

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

            MoveWindow(_currentWindow,x,y,w,h,true);

            App.Logger.WriteLine(LOG_IDENT, $"Updated Window Properties");
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
    }
}
