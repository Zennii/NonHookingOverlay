using SlimDX;
using SlimDX.Direct3D9;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NonHookingFormOverlay
{
    public partial class Overlay : Form
    {
        private string processName = "asd"; // Change this to your process name or window title. Or at least the first part of the window title. Case-sensitive.

        #region DLLImports/Etc
        /// <summary>
        /// Helper class containing User32 API functions
        /// </summary>
        private class User32
        {
            [StructLayout(LayoutKind.Sequential)]
            public struct RECT
            {
                public int left;
                public int top;
                public int right;
                public int bottom;
            }

            public struct POINT
            {
                public int x;
                public int y;
            }

            [DllImport("user32.dll")]
            public static extern IntPtr GetDesktopWindow();
            [DllImport("user32.dll")]
            public static extern IntPtr GetWindowDC(IntPtr hWnd);
            [DllImport("user32.dll")]
            public static extern IntPtr ReleaseDC(IntPtr hWnd, IntPtr hDC);
            [return: MarshalAs(UnmanagedType.Bool)]
            [DllImport("user32.dll")]
            public static extern bool GetClientRect(IntPtr hWnd, ref RECT rect);
            [DllImport("user32.dll")]
            public static extern bool ClientToScreen(IntPtr hWnd, ref POINT rect);
            [DllImport("user32.dll", SetLastError = true)]
            public static extern UInt32 GetWindowLong(IntPtr hWnd, int nIndex);
            [DllImport("user32.dll")]
            public static extern int SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
            [DllImport("user32.dll")]
            public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
            [DllImport("user32.dll")]
            public static extern IntPtr GetForegroundWindow();
        }
        [DllImport("dwmapi.dll")]
        static extern void DwmExtendFrameIntoClientArea(IntPtr hWnd, ref Margins pMargins);

        public const int GWL_EXSTYLE = -20;

        public const int WS_EX_LAYERED = 0x80000;

        public const int WS_EX_TRANSPARENT = 0x20;

        public const int LWA_ALPHA = 0x2;

        public const int LWA_COLORKEY = 0x1;
        #endregion

        // This is used to specify the boundaries of the transparent area
        internal struct Margins
        {
            public int Left, Right, Top, Bottom;
        }
        private Margins marg;

        private Device device = null;

        IntPtr ourHandle = IntPtr.Zero;

        // NOTE: I don't think this supports fullscreen applications. So keep that in mind.
        public Overlay()
        {
            InitializeComponent();

            Process[] procs = Process.GetProcessesByName(processName);
            if (procs.Length > 1)
            {
                MessageBox.Show("Error 1\nMultiple processes \"" + processName + "\" found. Program this yourself.", "Multiple processes", MessageBoxButtons.OK, MessageBoxIcon.Error);
                // TODO: Support choosing between multiple windows!
                Environment.Exit(0); // Kill it
            }
            else if (procs.Length > 0)
            {
                ourHandle = procs[0].MainWindowHandle; // NOTE: Sometimes might just be "procs[0].Handle". Change below too if needed.
            }

            if (ourHandle == IntPtr.Zero)
            {
                List<Process> procs2 = new List<Process>(Process.GetProcesses());
                foreach (Process p in procs2)
                {
                    if (p.MainWindowTitle.StartsWith(processName))
                    {
                        if ((ourHandle = p.MainWindowHandle) != IntPtr.Zero) // NOTE: Sometimes might just be "p.Handle"
                        {
                            break;
                        }
                    }
                }
            }

            if (ourHandle == IntPtr.Zero) // No window?
            {
                MessageBox.Show("Error 2\nNo Process \"" + processName + "\" found.", "No process found", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(0);
            }

            //Make the window's border completely transparant
            User32.SetWindowLong(this.Handle, GWL_EXSTYLE,
                    (IntPtr)(User32.GetWindowLong(this.Handle, GWL_EXSTYLE) ^ WS_EX_LAYERED ^ WS_EX_TRANSPARENT));

            // Set the Alpha on the Whole Window (Black) to 255 (solid)
            //SetLayeredWindowAttributes(this.Handle, (uint)System.Drawing.ColorTranslator.ToWin32(Color.Magenta), 255, LWA_ALPHA); // Color example
            User32.SetLayeredWindowAttributes(this.Handle, 0, 255, LWA_ALPHA);
            // The frame is black due to aliasing or something causing some outline issues sometimes. I think. I can't remember, this code is old. I just want it for future reference.

            User32.RECT windowRect = new User32.RECT();
            User32.GetClientRect(ourHandle, ref windowRect);
            User32.POINT pt = new User32.POINT();
            User32.ClientToScreen(ourHandle, ref pt);
            this.Width = windowRect.right - windowRect.left;
            this.Height = windowRect.bottom - windowRect.top;
            this.Location = new Point(pt.x, pt.y);

            // Create a margin (the whole form)
            marg = new Margins
            {
                Left = 0,
                Top = 0,
                Right = this.Width,
                Bottom = this.Height
            }; // Obviously this will have to move if you want your form to resize

            // Init DirectX
            // This initializes the DirectX device. It needs to be done once.
            // The alpha channel in the backbuffer is critical.
            PresentParameters presentParameters = new PresentParameters();
            presentParameters.Windowed = true;
            presentParameters.SwapEffect = SwapEffect.Discard;
            presentParameters.BackBufferFormat = Format.A8R8G8B8;
            presentParameters.BackBufferWidth = this.Width;
            presentParameters.BackBufferHeight = this.Height;
            this.device = new Device(new Direct3D(), 0, DeviceType.Hardware, this.Handle,
            CreateFlags.HardwareVertexProcessing, presentParameters);

            Thread directx = new Thread(new ThreadStart(this.dxThread));
            directx.IsBackground = true;
            directx.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // Expand the Aero Glass Effect Border to the WHOLE form.
            // since we have already had the border invisible we now
            // have a completely invisible window - apart from the DirectX
            // renders NOT in black.
            DwmExtendFrameIntoClientArea(this.Handle, ref marg);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Do nothing here to stop window normal background painting
        }

        private void Overlay_Load(object sender, EventArgs e)
        {

        }

        private void dxThread()
        {
            while (true)
            {
                // All drawing in here.

                device.BeginScene();
                {
                    device.Clear(ClearFlags.Target, Color.FromArgb(0, 0, 0, 0), 1.0f, 0);
                    if (User32.GetForegroundWindow() == ourHandle) // Don't draw if we're not focused on the window.
                    {
                        DrawRect(new Vector2(100,100), 100, 100, Color.Red);
                    }
                }
                device.EndScene();
                device.Present();
            }
            this.device.Dispose();
            Application.Exit();
        }

        public void DrawLine(Vector2 pt1, Vector2 pt2, float LineWidth, Color color)
        {
            Line line1 = new Line(device);
            Vector2[] l = new Vector2[] { pt1, pt2 };
            line1.Width = LineWidth;
            line1.Draw(l, color);
            line1.Dispose();
        }

        // I'm not great at DirectX, so here's my lame attempt at drawing a rectangle using a line. You'll have to do everything else yourself.
        public void DrawRect(Vector2 pt, float width, float height, Color color)
        {
            float w2 = width / 2;
            float h2 = height / 2;
            Line line1 = new Line(device);
            Vector2[] l = new Vector2[] { new Vector2(pt[0]-w2, pt[1]), new Vector2(pt[0]+w2, pt[1]) };
            line1.Width = height;
            line1.Draw(l, color);
            line1.Dispose();
        }

        // - EOF -
    }
}
