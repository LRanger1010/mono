//
// System.Drawing.carbonFunctions.cs
//
// Authors:
//      Geoff Norton (gnorton@customerdna.com>
//
// Copyright (C) 2007 Novell, Inc. (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;

namespace System.Drawing {

	[SuppressUnmanagedCodeSecurity]
	internal class Carbon {
		internal static Hashtable contextReference = new Hashtable ();
		internal static object lockobj = new object ();

		internal static Delegate hwnd_delegate;

		static Carbon () {
			foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies ()) {
				if (String.Equals (asm.GetName ().Name, "System.Windows.Forms")) {
					Type driver_type = asm.GetType ("System.Windows.Forms.XplatUICarbon");
					if (driver_type != null) {
						hwnd_delegate = (Delegate) driver_type.GetField ("HwndDelegate", BindingFlags.NonPublic | BindingFlags.Static).GetValue (null);
					}
				}
			}
		}

		internal static CarbonContext GetCGContextForNSView (IntPtr handle) {
			IntPtr context = IntPtr.Zero;
			Rect view_bounds = new Rect ();

			context = objc_msgSend (objc_msgSend (objc_getClass ("NSGraphicsContext"), sel_registerName ("currentContext")), sel_registerName ("graphicsPort"));
			objc_msgSend_stret (ref view_bounds, handle, sel_registerName ("bounds"));
			return new CarbonContext (IntPtr.Zero, context, (int)view_bounds.size.width, (int)view_bounds.size.height);
		}

		internal static CarbonContext GetCGContextForView (IntPtr handle) {
			IntPtr context = IntPtr.Zero;
			IntPtr port = IntPtr.Zero;
			IntPtr window = IntPtr.Zero;

			QDRect window_bounds = new QDRect ();
			Rect view_bounds = new Rect ();

			window = GetControlOwner (handle);
			port = GetWindowPort (window);
			
			context = GetContext (port);

			GetWindowBounds (window, 32, ref window_bounds);

			HIViewGetBounds (handle, ref view_bounds);

			HIViewConvertRect (ref view_bounds, handle, IntPtr.Zero);

			CGContextTranslateCTM (context, view_bounds.origin.x, (window_bounds.bottom - window_bounds.top) - (view_bounds.origin.y + view_bounds.size.height));

			// Create the original rect path and clip to it
			IntPtr clip_path = CGPathCreateMutable ();
			Rect rc_clip = new Rect (0, 0, view_bounds.size.width, view_bounds.size.height);
			CGPathAddRect (clip_path, IntPtr.Zero, rc_clip);
			CGContextBeginPath (context);

			Rectangle [] clip_rectangles = (Rectangle []) hwnd_delegate.DynamicInvoke (new object [] {handle});
			if (clip_rectangles != null) {
				int length = clip_rectangles.Length;
				Rect [] clip_rects = new Rect [length];
				for (int i = 0; i < length; i++) {
					Rectangle r = (Rectangle) clip_rectangles [i];
					clip_rects [i] = new Rect ();
					clip_rects [i].origin.x = r.X; 
					clip_rects [i].origin.y = view_bounds.size.height - r.Y - r.Height; 
					clip_rects [i].size.width = r.Width; 
					clip_rects [i].size.height = r.Height; 
				}
				CGPathAddRects (clip_path, IntPtr.Zero, clip_rects, length);
				CGContextAddPath (context, clip_path);
				CGContextEOClip (context);
			}

			return new CarbonContext (port, context, (int)view_bounds.size.width, (int)view_bounds.size.height);
		}

		internal static IntPtr GetContext (IntPtr port) {
			IntPtr context = IntPtr.Zero;

			lock (lockobj) { 
				if (contextReference [port] != null) {
					CreateCGContextForPort (port, ref context);
				} else {
					QDBeginCGContext (port, ref context);
					contextReference [port] = context;
				}
			}

			return context;
		}

		internal static void ReleaseContext (IntPtr port, IntPtr context) {
			lock (lockobj) { 
				if (contextReference [port] != null && context == (IntPtr) contextReference [port]) { 
					QDEndCGContext (port, ref context);
					contextReference [port] = null;
				} else {
					CFRelease (context);
				}
			}
		}

		#region Cocoa Methods
		[DllImport("libobjc.dylib")]
		public static extern IntPtr objc_getClass(string className); 
		[DllImport("libobjc.dylib")]
		public static extern IntPtr objc_msgSend(IntPtr basePtr, IntPtr selector, string argument);  
		[DllImport("libobjc.dylib")]
		public static extern IntPtr objc_msgSend(IntPtr basePtr, IntPtr selector);        
		[DllImport("libobjc.dylib")]
		public static extern void objc_msgSend_stret(ref Rect arect, IntPtr basePtr, IntPtr selector);        
		[DllImport("libobjc.dylib")]
		public static extern IntPtr sel_registerName(string selectorName);         
		#endregion

		[DllImport("/System/Library/Frameworks/Carbon.framework/Versions/Current/Carbon")]
		internal static extern int HIViewGetBounds (IntPtr vHnd, ref Rect r);
		[DllImport("/System/Library/Frameworks/Carbon.framework/Versions/Current/Carbon")]
		internal static extern int HIViewConvertRect (ref Rect r, IntPtr a, IntPtr b);

		[DllImport ("/System/Library/Frameworks/Carbon.framework/Versions/Current/Carbon")]
		internal static extern IntPtr GetControlOwner (IntPtr aView);

		[DllImport("/System/Library/Frameworks/Carbon.framework/Versions/Current/Carbon")]
		internal static extern int GetWindowBounds (IntPtr wHnd, uint reg, ref QDRect rect);
		[DllImport ("/System/Library/Frameworks/Carbon.framework/Versions/Current/Carbon")]
		internal static extern IntPtr GetWindowPort (IntPtr hWnd);
		[DllImport ("/System/Library/Frameworks/Carbon.framework/Versions/Current/Carbon")]
		internal static extern void CreateCGContextForPort (IntPtr port, ref IntPtr context);
		[DllImport ("/System/Library/Frameworks/Carbon.framework/Versions/Current/Carbon")]
		internal static extern void CFRelease (IntPtr context);
		[DllImport ("/System/Library/Frameworks/Carbon.framework/Versions/Current/Carbon")]
		internal static extern void QDBeginCGContext (IntPtr port, ref IntPtr context);
		[DllImport ("/System/Library/Frameworks/Carbon.framework/Versions/Current/Carbon")]
		internal static extern void QDEndCGContext (IntPtr port, ref IntPtr context);
		[DllImport ("/System/Library/Frameworks/Carbon.framework/Versions/Current/Carbon")]
		internal static extern int CGContextClipToRect (IntPtr context, Rect clip);
		[DllImport ("/System/Library/Frameworks/Carbon.framework/Versions/Current/Carbon")]
		internal static extern int CGContextClipToRects (IntPtr context, Rect [] clip_rects, int count);
		[DllImport ("/System/Library/Frameworks/Carbon.framework/Versions/Current/Carbon")]
		internal static extern void CGContextTranslateCTM (IntPtr context, float tx, float ty);
		[DllImport ("/System/Library/Frameworks/Carbon.framework/Versions/Current/Carbon")]
		internal static extern void CGContextScaleCTM (IntPtr context, float x, float y);
		[DllImport ("/System/Library/Frameworks/Carbon.framework/Versions/Current/Carbon")]
		internal static extern void CGContextFlush (IntPtr context);
		[DllImport ("/System/Library/Frameworks/Carbon.framework/Versions/Current/Carbon")]
		internal static extern void CGContextSynchronize (IntPtr context);
		[DllImport ("/System/Library/Frameworks/Carbon.framework/Versions/Current/Carbon")]
		internal static extern IntPtr CGPathCreateMutable ();
		[DllImport ("/System/Library/Frameworks/Carbon.framework/Versions/Current/Carbon")]
		internal static extern void CGPathAddRects (IntPtr path, IntPtr _void, Rect [] rects, int count);
		[DllImport ("/System/Library/Frameworks/Carbon.framework/Versions/Current/Carbon")]
		internal static extern void CGPathAddRect (IntPtr path, IntPtr _void, Rect rect);
		[DllImport ("/System/Library/Frameworks/Carbon.framework/Versions/Current/Carbon")]
		internal static extern void CGContextBeginPath (IntPtr context);
		[DllImport ("/System/Library/Frameworks/Carbon.framework/Versions/Current/Carbon")]
		internal static extern void CGContextAddPath (IntPtr context, IntPtr path);
		[DllImport ("/System/Library/Frameworks/Carbon.framework/Versions/Current/Carbon")]
		internal static extern void CGContextClip (IntPtr context);
		[DllImport ("/System/Library/Frameworks/Carbon.framework/Versions/Current/Carbon")]
		internal static extern void CGContextEOClip (IntPtr context);
		[DllImport ("/System/Library/Frameworks/Carbon.framework/Versions/Current/Carbon")]
		internal static extern void CGContextEOFillPath (IntPtr context);
	}

	internal struct CGSize {
		public float width;
		public float height;
	}

	internal struct CGPoint {
		public float x;
		public float y;
	}

	internal struct Rect {
		public Rect (float x, float y, float width, float height) {
			this.origin.x = x;
			this.origin.y = y;
			this.size.width = width;
			this.size.height = height;
		}

		public CGPoint origin;
		public CGSize size;
	}

	internal struct QDRect
	{
		public short top;
		public short left;
		public short bottom;
		public short right;
	}

	internal struct CarbonContext
	{
		public IntPtr port;
		public IntPtr ctx;
		public int width;
		public int height;

		public CarbonContext (IntPtr port, IntPtr ctx, int width, int height)
		{
			this.port = port;
			this.ctx = ctx;
			this.width = width;
			this.height = height;
		}
	}
}
