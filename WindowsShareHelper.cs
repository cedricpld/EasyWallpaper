using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace CGWallpaper;

public static class WindowsShareHelper
{
    [ComImport]
    [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataTransferManagerInterop
    {
        IntPtr GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);
        void ShowShareUIForWindow(IntPtr appWindow);
    }

    private static readonly Guid DataTransferManagerIid = new("A5CAEE9B-8708-49D1-8D36-67D25A8DA00C");
    private static string? _pendingTitle;
    private static string? _pendingFilePath;
    private static string? _pendingJson;
    private static DataTransferManager? _dtm;

    public static void ShareProfile(Window owner, string profileName, string filePath, string jsonText)
    {
        IntPtr hwnd = new WindowInteropHelper(owner).Handle;
        var interop = DataTransferManager.As<IDataTransferManagerInterop>();
        Guid iid = DataTransferManagerIid;
        IntPtr dtmPtr = interop.GetForWindow(hwnd, ref iid);
        var dtm = WinRT.MarshalInspectable<DataTransferManager>.FromAbi(dtmPtr);

        _pendingTitle = $"EasyWallpaper — {profileName}";
        _pendingFilePath = filePath;
        _pendingJson = jsonText;

        if (_dtm != dtm)
        {
            if (_dtm != null) _dtm.DataRequested -= OnDataRequested;
            _dtm = dtm;
            _dtm.DataRequested += OnDataRequested;
        }

        interop.ShowShareUIForWindow(hwnd);
    }

    private static async void OnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
    {
        var deferral = args.Request.GetDeferral();
        try
        {
            var data = args.Request.Data;
            data.Properties.Title = _pendingTitle ?? "EasyWallpaper Profile";
            data.Properties.Description = "Profil de fonds d'écran EasyWallpaper";

            if (!string.IsNullOrEmpty(_pendingFilePath) && File.Exists(_pendingFilePath))
            {
                var file = await StorageFile.GetFileFromPathAsync(_pendingFilePath);
                data.SetStorageItems([file]);
            }
            if (!string.IsNullOrEmpty(_pendingJson))
            {
                data.SetText(_pendingJson);
            }
        }
        catch { }
        finally
        {
            deferral.Complete();
        }
    }
}
