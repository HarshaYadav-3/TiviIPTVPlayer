using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Media.Playback;


namespace TiviIPTVPlayer.Services
{
    /// <summary>
    /// Captures the current video frame of a MediaPlayer into a PNG file.
    /// Creates a D3D11 render-target texture, bridges it to a WinRT
    /// IDirect3DSurface, copies the frame with MediaPlayer.CopyFrameToVideoSurface
    /// and encodes with the WinRT BitmapEncoder (no extra packages).
    /// </summary>
    public static class SnapshotService
    {
        // ----------------------------------------------------------- D3D interop

        private const int D3D11_SDK_VERSION = 7;
        private const int D3D_DRIVER_TYPE_HARDWARE = 1;
        private const int D3D_DRIVER_TYPE_WARP = 5;

        private const int DXGI_FORMAT_B8G8R8A8_UNORM = 87;
        private const int D3D11_USAGE_DEFAULT = 0;
        private const uint D3D11_BIND_RENDER_TARGET = 0x20;
        private const uint D3D11_BIND_SHADER_RESOURCE = 0x8;

        // ID3D11Device vtable: IUnknown(0..2), CreateBuffer(3), CreateTexture1D(4), CreateTexture2D(5)
        private const int CreateTexture2DSlot = 5;

        private static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        private static readonly Guid IID_IDXGISurface = new("cafcb56c-6ac3-4889-bf47-9e23bbd260ec");

        [DllImport("d3d11.dll", SetLastError = true)]
        private static extern int D3D11CreateDevice(
            IntPtr adapter,
            int driverType,
            IntPtr software,
            uint flags,
            IntPtr featureLevels,
            uint numFeatureLevels,
            uint sdkVersion,
            out IntPtr device,
            out int featureLevel,
            out IntPtr immediateContext);

        [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11SurfaceFromDXGISurface",
            SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true,
            CallingConvention = CallingConvention.StdCall)]
        private static extern uint CreateDirect3D11SurfaceFromDXGISurface(
            IntPtr dxgiSurface, out IntPtr graphicsSurface);

        [StructLayout(LayoutKind.Sequential)]
        private struct D3D11Texture2DDesc
        {
            public uint Width;
            public uint Height;
            public uint MipLevels;
            public uint ArraySize;
            public int Format;
            public uint SampleDescCount;
            public uint SampleDescQuality;
            public int Usage;
            public uint BindFlags;
            public uint CPUAccessFlags;
            public uint MiscFlags;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateTexture2DDelegate(
            IntPtr self,
            ref D3D11Texture2DDesc desc,
            IntPtr initialData,
            out IntPtr texture2D);

        // -------------------------------------------------------------- capture

        /// <summary>Saves the current frame of the player to the user's Pictures folder.</summary>
        /// <returns>Full path of the written PNG file.</returns>
        public static async Task<string> CaptureAsync(MediaPlayer player, string channelName)
        {
            if (player == null || player.PlaybackSession == null)
            {
                throw new InvalidOperationException("No media player available yet.");
            }

            var session = player.PlaybackSession;
            int width = (int)session.NaturalVideoWidth;
            int height = (int)session.NaturalVideoHeight;
            if (width <= 0 || height <= 0)
            {
                throw new InvalidOperationException(
                    "No video frame is available yet. Wait for the channel to start playing and try again.");
            }

            IntPtr devicePtr = IntPtr.Zero;
            IntPtr contextPtr = IntPtr.Zero;
            IntPtr texturePtr = IntPtr.Zero;

            try
            {
                devicePtr = CreateD3DDevice();
                if (devicePtr == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Could not create a Direct3D device for the snapshot.");
                }

                var desc = new D3D11Texture2DDesc
                {
                    Width = (uint)width,
                    Height = (uint)height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = DXGI_FORMAT_B8G8R8A8_UNORM,
                    SampleDescCount = 1,
                    Usage = D3D11_USAGE_DEFAULT,
                    BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE,
                };

                if (TryCreateTexture2D(devicePtr, ref desc, out texturePtr) < 0 || texturePtr == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Could not create the snapshot render target.");
                }

                var dxgiSurfacePtr = QueryInterface(texturePtr, IID_IDXGISurface);
                try
                {
                    var hr = CreateDirect3D11SurfaceFromDXGISurface(dxgiSurfacePtr, out var surfacePtr);
                    if (hr != 0 || surfacePtr == IntPtr.Zero)
                    {
                        throw new InvalidOperationException("Could not bridge the surface to WinRT on this system.");
                    }

                    // Wrap the raw COM pointer in the projected WinRT interface;
                    // GetObjectForIUnknown + cast cannot be marshaled back to native.
                    var surface = WinRT.MarshalInterface<IDirect3DSurface>.FromAbi(surfacePtr);
                    player.CopyFrameToVideoSurface(surface);

                    var softwareBitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(surface);
                    var path = await SaveBitmapAsync(softwareBitmap, channelName);
                    return path;
                }
                finally
                {
                    Marshal.Release(dxgiSurfacePtr);
                }
            }
            finally
            {
                if (texturePtr != IntPtr.Zero)
                {
                    Marshal.Release(texturePtr);
                }
                if (contextPtr != IntPtr.Zero)
                {
                    Marshal.Release(contextPtr);
                }
                if (devicePtr != IntPtr.Zero)
                {
                    Marshal.Release(devicePtr);
                }
            }
        }

        private static int TryCreateTexture2D(IntPtr devicePtr, ref D3D11Texture2DDesc desc, out IntPtr texturePtr)
        {
            var vtbl = Marshal.ReadIntPtr(devicePtr);
            var proc = Marshal.ReadIntPtr(vtbl, CreateTexture2DSlot * IntPtr.Size);
            var createTexture2D = Marshal.GetDelegateForFunctionPointer<CreateTexture2DDelegate>(proc);
            return createTexture2D(devicePtr, ref desc, IntPtr.Zero, out texturePtr);
        }

        private static async Task<string> SaveBitmapAsync(SoftwareBitmap bitmap, string channelName)
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "TiviIPTVPlayer");

            var safe = SanitizeFileName(channelName);
            if (string.IsNullOrEmpty(safe))
            {
                safe = "snapshot";
            }

            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            // FileRandomAccessStream.OpenAsync cannot create files, so create
            // the target through StorageFolder first.
            Directory.CreateDirectory(dir);
            var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(dir);
            var file = await folder.CreateFileAsync(
                $"{safe}_{stamp}.png",
                Windows.Storage.CreationCollisionOption.GenerateUniqueName);

            using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            var converted = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            encoder.SetSoftwareBitmap(converted);
            await encoder.FlushAsync();

            return file.Path;
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0)
                {
                    chars[i] = '_';
                }
            }

            var cleaned = new string(chars);
            return cleaned.Length > 60 ? cleaned.Substring(0, 60) : cleaned;
        }

        private static IntPtr CreateD3DDevice()
        {
            foreach (var driverType in new[] { D3D_DRIVER_TYPE_HARDWARE, D3D_DRIVER_TYPE_WARP })
            {
                var hr = D3D11CreateDevice(
                    IntPtr.Zero, driverType, IntPtr.Zero, 0,
                    IntPtr.Zero, 0, D3D11_SDK_VERSION,
                    out var device, out _, out _);
                if (hr == 0 && device != IntPtr.Zero)
                {
                    return device;
                }
            }

            return IntPtr.Zero;
        }

        private static IntPtr QueryInterface(IntPtr comObject, Guid iid)
        {
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(comObject, ref iid, out var ptr));
            return ptr;
        }
    }
}
