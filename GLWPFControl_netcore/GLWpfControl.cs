﻿using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Platform;

namespace OpenTK.Wpf {
    /// <summary>
    ///     Provides a native WPF control for OpenTK.
    ///     To use this component, call the <see cref="Start(GLWpfControlSettings)"/> method.
    ///     Bind to the <see cref="Render"/> event only after <see cref="Start(GLWpfControlSettings)"/> is called.
    /// </summary>
    public sealed class GLWpfControl : FrameworkElement {
        private const int ResizeUpdateInterval = 1;

        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private long _resizeStartStamp;
        private TimeSpan _lastFrameStamp;

        private IGraphicsContext _context;
        private IWindowInfo _windowInfo;

        private GLWpfControlSettings _settings;
        private GLWpfControlRenderer _renderer;
        private HwndSource _hwnd;

        /// Called whenever rendering should occur.
        public event Action<TimeSpan> Render;

        /// <summary>
        /// Gets called after the control has finished initializing and is ready to render
        /// </summary>
        public event Action Ready;

        // The image that the control uses
        private readonly System.Windows.Controls.Image _image;

        // Transformations and size 
        private TranslateTransform _translateTransform;
        private Rect _imageRectangle;

        static GLWpfControl() {
            Toolkit.Init(new ToolkitOptions {
                Backend = PlatformBackend.PreferNative
            });
        }

        /// The OpenGL Framebuffer Object used internally by this component.
        /// Bind to this instead of the default framebuffer when using this component along with other FrameBuffers for the final pass.
        public int FrameBuffer => _renderer?.FrameBuffer ?? 0;

        /// <summary>
        ///     Used to create a new control. Before rendering can take place, <see cref="Start(GLWpfControlSettings)"/> must be called.
        /// </summary>
        public GLWpfControl() {
            _image = new System.Windows.Controls.Image() {
                Stretch = Stretch.Fill,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform() {
                    ScaleY = -1
                }
            };
        }

        /// Starts the control and rendering, using the settings provided.
        public void Start(GLWpfControlSettings settings) {
            _settings = settings;
            IsVisibleChanged += (_, args) => {
                if ((bool)args.NewValue) {
                    CompositionTarget.Rendering += OnCompTargetRender;
                } else {
                    CompositionTarget.Rendering -= OnCompTargetRender;
                }
            };

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs args) {
            if (_context != null) {
                return;
            }
            if (_settings.ContextToUse == null) {
                var window = Window.GetWindow(this);
                var baseHandle = window is null ? IntPtr.Zero : new WindowInteropHelper(window).Handle;
                _hwnd = new HwndSource(0, 0, 0, 0, 0, "GLWpfControl", baseHandle);
                _windowInfo = Utilities.CreateWindowsWindowInfo(_hwnd.Handle);

                var mode = new GraphicsMode(ColorFormat.Empty, 0, 0, 0, 0, 0, false);
                _context = new GraphicsContext(mode, _windowInfo, _settings.MajorVersion, _settings.MinorVersion,
                    _settings.GraphicsContextFlags);
                _context.LoadAll();
                _context.MakeCurrent(_windowInfo);
            } else {
                _context = _settings.ContextToUse;
            }

            if (_renderer == null) {
                var width = (int)RenderSize.Width;
                var height = (int)RenderSize.Height;
                _renderer = new GLWpfControlRenderer(width, height, _image, _settings.UseHardwareRender, _settings.PixelBufferObjectCount);
            }

            _imageRectangle = new Rect(0, 0, RenderSize.Width, RenderSize.Height);
            _translateTransform = new TranslateTransform(0, RenderSize.Height);

            GL.Viewport(0, 0, (int)RenderSize.Width, (int)RenderSize.Height);
            Ready?.Invoke();
        }


        private void OnUnloaded(object sender, RoutedEventArgs args) {
            if (_context == null) {
                return;
            }

            ReleaseOpenGLResources();
            _windowInfo?.Dispose();
            _hwnd?.Dispose();
        }

        private void OnCompTargetRender(object sender, EventArgs e) {
            if (_context == null || _renderer == null) {
                return;
            }
            if (!ReferenceEquals(GraphicsContext.CurrentContext, _context)) {
                _context?.MakeCurrent(_windowInfo);
            }
            if (_resizeStartStamp != 0) {
                if (_resizeStartStamp + ResizeUpdateInterval > _stopwatch.ElapsedMilliseconds) {
                    return;
                }
                _renderer?.DeleteBuffers();
                var width = (int)RenderSize.Width;
                var height = (int)RenderSize.Height;
                _renderer = new GLWpfControlRenderer(width, height, _image, _settings.UseHardwareRender, _settings.PixelBufferObjectCount);
                _resizeStartStamp = 0;
            }


            GL.BindFramebuffer(FramebufferTarget.Framebuffer, FrameBuffer);

            TimeSpan deltaTime = _stopwatch.Elapsed - _lastFrameStamp;

            Render?.Invoke(deltaTime);

            _renderer.UpdateImage();
            InvalidateVisual();
            _lastFrameStamp = _stopwatch.Elapsed;
        }

        protected override void OnRender(DrawingContext drawingContext) {
            // Transforms are applied in reverse order
            drawingContext.PushTransform(_translateTransform);              // Apply translation to the image on the Y axis by the height. This assures that in the next step, where we apply a negative scale the image is still inside of the window
            drawingContext.PushTransform(_image.RenderTransform);           // Apply a scale where the Y axis is -1. This will rotate the image by 180 deg

            drawingContext.DrawImage(_image.Source, _imageRectangle);       // Draw the image source 

            drawingContext.Pop();                                           // Remove the scale transform
            drawingContext.Pop();                                           // Remove the translation transform

            base.OnRender(drawingContext);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo info) {
            if (_renderer == null) {
                return;
            }

            _resizeStartStamp = _stopwatch.ElapsedMilliseconds;

            bool invalidate = false;
            if (info.HeightChanged) {
                _imageRectangle.Height = info.NewSize.Height;
                _translateTransform.Y = info.NewSize.Height;
                invalidate = true;
            }
            if (info.WidthChanged) {
                _imageRectangle.Width = info.NewSize.Width;
                invalidate = true;
            }
            if (invalidate) InvalidateVisual();
            GL.Viewport(0, 0, (int)RenderSize.Width, (int)RenderSize.Height);
            base.OnRenderSizeChanged(info);
        }

        private void ReleaseOpenGLResources() {
            _renderer?.DeleteBuffers();
            if (!_settings.IsUsingExternalContext) {
                _context?.Dispose();
                _context = null;
            }
        }
    }
}
