﻿using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using PixelFormat = OpenTK.Graphics.OpenGL.PixelFormat;

namespace OpenTK.Wpf {
    internal sealed class GLWpfControlRenderer {

        private readonly WriteableBitmap _bitmap;
        private readonly int _colorBuffer;
        private readonly int _depthBuffer;

        private readonly System.Windows.Controls.Image _imageControl;
        private readonly bool _isHardwareRenderer;
        private readonly int[] _pixelBuffers;
        private bool _hasRenderedAFrame = false;

        public int FrameBuffer { get; }

        public int Width => _bitmap.PixelWidth;
        public int Height => _bitmap.PixelHeight;
        public int PixelBufferObjectCount => _pixelBuffers.Length;

        public GLWpfControlRenderer(int width, int height, System.Windows.Controls.Image imageControl, bool isHardwareRenderer, int pixelBufferCount) {

            _imageControl = imageControl;
            _isHardwareRenderer = isHardwareRenderer;
            // the bitmap we're blitting to in software mode.
            _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);

            // set up the framebuffer
            FrameBuffer = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, FrameBuffer);

            _depthBuffer = GL.GenRenderbuffer();
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthBuffer);
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent24, width, height);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                RenderbufferTarget.Renderbuffer, _depthBuffer);

            _colorBuffer = GL.GenRenderbuffer();
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _colorBuffer);
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Rgba8, width, height);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                RenderbufferTarget.Renderbuffer, _colorBuffer);

            var error = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (error != FramebufferErrorCode.FramebufferComplete) {
                throw new GraphicsErrorException("Error creating frame buffer: " + error);
            }

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

            // generate the pixel buffers

            _pixelBuffers = new int[pixelBufferCount];
            // RGBA8 buffer
            var size = sizeof(byte) * 4 * width * height;
            for (var i = 0; i < _pixelBuffers.Length; i++) {
                var pb = GL.GenBuffer();
                GL.BindBuffer(BufferTarget.PixelPackBuffer, pb);
                GL.BufferData(BufferTarget.PixelPackBuffer, size, IntPtr.Zero, BufferUsageHint.StreamRead);
                _pixelBuffers[i] = pb;
            }

            GL.BindBuffer(BufferTarget.PixelPackBuffer, 0);
        }

        public void DeleteBuffers() {
            GL.DeleteFramebuffer(FrameBuffer);
            GL.DeleteRenderbuffer(_depthBuffer);
            GL.DeleteRenderbuffer(_colorBuffer);
            for (var i = 0; i < _pixelBuffers.Length; i++) {
                GL.DeleteBuffer(_pixelBuffers[i]);
            }
        }

        // shifts all of the PBOs along by 1.
        private void RotatePixelBuffers() {
            var fst = _pixelBuffers[0];
            for (var i = 1; i < _pixelBuffers.Length; i++) {
                _pixelBuffers[i - 1] = _pixelBuffers[i];
            }
            _pixelBuffers[_pixelBuffers.Length - 1] = fst;
        }

        public void UpdateImage() {
            if (false && _isHardwareRenderer) {
                UpdateImageHardware();
            } else {
                UpdateImageSoftware();
            }

            _hasRenderedAFrame = true;
        }



        private void UpdateImageSoftware() {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, FrameBuffer);
            // start the (async) pixel transfer.
            GL.BindBuffer(BufferTarget.PixelPackBuffer, _pixelBuffers[0]);
            GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
            GL.ReadPixels(0, 0, Width, Height, PixelFormat.Bgra, PixelType.UnsignedByte, IntPtr.Zero);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            // rotate the pixel buffers.
            if (_hasRenderedAFrame) {
                RotatePixelBuffers();
            }

            GL.BindBuffer(BufferTarget.PixelPackBuffer, _pixelBuffers[0]);
            // copy the data over from a mapped buffer.
            _bitmap.Lock();
            var data = GL.MapBuffer(BufferTarget.PixelPackBuffer, BufferAccess.ReadOnly);
            var len = (uint)(sizeof(byte) * 4 * Width * Height); 
            unsafe {
                System.Buffer.MemoryCopy((void*)data, (void*)_bitmap.BackBuffer, len, len);
            }
            var err = GL.GetError();
            Debug.WriteLine(err);
            _bitmap.AddDirtyRect(new Int32Rect(0, 0, _bitmap.PixelWidth, _bitmap.PixelHeight));
            _bitmap.Unlock();
            GL.UnmapBuffer(BufferTarget.PixelPackBuffer);
            if (!ReferenceEquals(_imageControl.Source, _bitmap)) {
                _imageControl.Source = _bitmap;
            }

            GL.BindBuffer(BufferTarget.PixelPackBuffer, 0);
        }
        private void UpdateImageHardware() {
            // There are 2 options we can use here.
            // 1. Use a D3DSurface and WGL_NV_DX_interop to perform the rendering.
            //         This is still performing RTT (render to texture) and isn't as fast as just directly drawing the stuff onto the DX buffer.
            // 2. Steal the handles using hooks into DirectX, then use that to directly render.
            //         This is the fastest possible way, but it requires a whole lot of moving parts to get anything working properly.

            // references for (2):

            // Accessing WPF's Direct3D internals.
            // note: see the WPFD3dHack.zip file on the blog post
            // http://jmorrill.hjtcentral.com/Home/tabid/428/EntryId/438/How-to-get-access-to-WPF-s-internal-Direct3D-guts.aspx

            // Using API hooks from C# to get d3d internals
            // this would have to be adapted to WPF, but should/maybe work.
            // http://spazzarama.com/2011/03/14/c-screen-capture-and-overlays-for-direct3d-9-10-and-11-using-api-hooks/
            // https://github.com/spazzarama/Direct3DHook
            throw new NotImplementedException();
        }

    }
}
