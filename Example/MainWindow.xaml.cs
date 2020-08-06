using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Wpf;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Example {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public sealed partial class MainWindow {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private TimeSpan _elapsedTime;

        public MainWindow() {
            InitializeComponent();
            _elapsedTime = new TimeSpan();
            var settings = new GLWpfControlSettings {
                MajorVersion = 2,
                MinorVersion = 1
            };
            OpenTkControl.Start(settings);
            InsetControl.Start(settings);

        }

        private void OpenTkControl_OnRender(TimeSpan delta) {
            _elapsedTime += delta;
            var c = Color4.FromHsv(new Vector4((float)_elapsedTime.TotalSeconds * 0.1f % 1, 1f, 0.9999f, 1));
            GL.ClearColor(c);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.Enable(EnableCap.ScissorTest);
            var xPos = 50 + (ActualWidth - 250) * (0.5 + 0.5 * Math.Sin(_stopwatch.Elapsed.TotalSeconds));
            GL.Scissor((int)xPos, 25, 50, 50);
            GL.ClearColor(Color4.Blue);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.Disable(EnableCap.ScissorTest);
            GL.LoadIdentity();
            GL.Begin(PrimitiveType.Triangles);
            GL.Color4(1.0f, 0.0f, 0.0f, 1.0f); GL.Vertex2(0.0f, 1.0f);
            GL.Color4(0.0f, 1.0f, 0.0f, 1.0f); GL.Vertex2(0.87f, -0.5f);
            GL.Color4(0.0f, 0.0f, 1.0f, 1.0f); GL.Vertex2(-0.87f, -0.5f);
            GL.End();
            GL.Finish();
        }
    }
}
