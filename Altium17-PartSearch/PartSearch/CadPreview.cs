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
        internal BitmapSource Symbol(int partId) => CopyBitmap(AltiumApi.GlobalVars.SCHServer.PaintLoadedComponentThumbnail(component, partId, 600, 340));
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
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    }
}
