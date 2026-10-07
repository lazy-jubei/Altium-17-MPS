using SCH;
using PCB;
using DXP;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Altium17PartSearch.PartSearch
{
    internal sealed class CadPreview
    {
        internal sealed class Footprint
        {
            public string Name { get; set; }
            internal string Path;
            internal bool IsCurrent;
        }
        private readonly ISch_Component component;
        internal int PartCount => Math.Max(1, component.GetState_PartCountNoPart0());
        internal List<Footprint> Footprints { get; } = new List<Footprint>();
        internal string Warning { get; private set; }
        internal CadPreview(LibraryModelChoice choice)
        {
            component = choice.Load() ?? throw new InvalidOperationException("Could not load the symbol preview.");
            var iterator = component.SchIterator_Create();
            if (iterator == null) return;
            try
            {
                iterator.AddFilter_ObjectSet(new SCH.TObjectSet(SCH.TObjectId.eImplementation));
                for (var item = iterator.FirstSchObject(); item != null; item = iterator.NextSchObject())
                {
                    var model = (ISch_Implementation)item;
                    if (!string.Equals(model.GetState_ModelType(), "PCBLIB", StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        var footprint = ResolveFootprint(model, choice);
                        if (footprint != null) Footprints.Add(footprint);
                    }
                    catch (Exception error)
                    {
                        RuntimeDiagnostics.Error("Footprint preview", error);
                        Warning = "Footprint preview: " + error.GetBaseException().Message;
                    }
                }
            }
            finally { component.SchIterator_Destroy(ref iterator); }
        }
        private static Footprint ResolveFootprint(ISch_Implementation model, LibraryModelChoice choice)
        {
            string path = null, cache = null;
            if (choice.CachedModels != null && Guid.TryParse(model.GetState_ModelVaultGUID(), out var vault) &&
                Guid.TryParse(model.GetState_ModelRevisionGUID(), out var revision))
                choice.CachedModels.TryGetValue(vault.ToString("D") + "/" + revision.ToString("D"), out cache);
            if (cache != null) path = CloudModelPolicy.FindModelFile(cache, model.GetState_ModelName(), "PCBLIB");
            else if (model.GetState_DatafileLinkCount() > 0) path = model.GetState_SchDatafileLink(0).GetState_Location();
            if (!File.Exists(path))
            {
                string found = "";
                path = EDP.Utils.LoadIntegratedLibraryManager()?.GetComponentDatafileLocation(0, model.GetState_ModelName(), "PCBLIB", choice.Reference, choice.LibraryPath, ref found);
                if (!File.Exists(path) && File.Exists(found)) path = found;
            }
            return File.Exists(path) ? new Footprint { Name = model.GetState_ModelName(), Path = path, IsCurrent = model.GetState_IsCurrent() } : null;
        }
        internal BitmapSource Symbol(int partId) => FitSymbol(CopyBitmap(AltiumApi.GlobalVars.SCHServer.PaintLoadedComponentThumbnail(component, partId, 600, 340)));
        internal BitmapSource Board(Footprint footprint)
        {
            var client = AltiumApi.GlobalVars.Client;
            client.StartServer("PCB");
            var server = client.GetServerModuleByName("PCB") as IPCB_ServerInterface
                ?? throw new InvalidOperationException("Altium's footprint renderer is unavailable.");
            return CopyBitmap(server.PaintFootprintThumbnail(footprint.Name, footprint.Path, 600, 340));
        }
        private static BitmapSource CopyBitmap(uint bitmap)
        {
            if (bitmap == 0) throw new InvalidOperationException("Altium did not return a CAD preview.");
            var handle = new IntPtr(unchecked((int)bitmap));
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                // Native GDI thumbnails have no meaningful alpha channel, including under Wine.
                var image = new FormatConvertedBitmap(source, PixelFormats.Bgr32, null, 0);
                image.Freeze();
                return image;
            }
            finally { DeleteObject(handle); }
        }
        private static BitmapSource FitSymbol(BitmapSource image)
        {
            // AD17's symbol renderer can leave large margins, especially at high DPI.
            // Trim the blank background so WPF can fit the drawing to the preview panel.
            int width = image.PixelWidth, height = image.PixelHeight, stride = width * 4;
            var pixels = new byte[stride * height];
            image.CopyPixels(pixels, stride, 0);
            int left = width, top = height, right = -1, bottom = -1;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int offset = y * stride + x * 4;
                    if (Math.Abs(pixels[offset] - pixels[0]) <= 8 &&
                        Math.Abs(pixels[offset + 1] - pixels[1]) <= 8 &&
                        Math.Abs(pixels[offset + 2] - pixels[2]) <= 8) continue;
                    left = Math.Min(left, x); top = Math.Min(top, y);
                    right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                }
            if (right < left) return image;
            int padding = Math.Max(8, Math.Max(right - left + 1, bottom - top + 1) / 12);
            left = Math.Max(0, left - padding); top = Math.Max(0, top - padding);
            right = Math.Min(width - 1, right + padding); bottom = Math.Min(height - 1, bottom + padding);
            var cropped = new CroppedBitmap(image, new Int32Rect(left, top, right - left + 1, bottom - top + 1));
            cropped.Freeze();
            return cropped;
        }
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    }
}
