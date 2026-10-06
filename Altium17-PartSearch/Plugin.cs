using DXP;
using SCH;
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CSharpPlugin
{
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public class PluginFactory
    {
        public object InvokePluginFactory(IClient client) => new Altium17PartSearch.ManufacturerPartSearchModule(client);
    }
}

namespace Altium17PartSearch
{
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public class ManufacturerPartSearchModule : ServerModule
    {
        private readonly bool noGUIMode;
        public ManufacturerPartSearchModule(IClient client) : base(client, "Altium17-PartSearch")
        {
            noGUIMode = client.ProductInfo().SupportsUIFeature("NoGUI", false);
        }
        protected override IServerDocument NewDocumentInstance(string kind, string fileName) => null;
        protected override void InitializeCommands()
        {
            ((CommandLauncher)CommandLauncher).RegisterCommand("Search", new CommandProc((IServerDocumentView view, ref string parameters) =>
            {
                try { RunManufacturerPartSearch(view, ref parameters); }
                catch (Exception error)
                {
                    RuntimeDiagnostics.Error("Manufacturer Part Search", error);
                    if (noGUIMode) throw;
                    MessageBox.Show(error.GetBaseException().Message, "Manufacturer Part Search Error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                }
            }));
        }

        private void RunManufacturerPartSearch(IServerDocumentView context, ref string parameters)
        {
            if (noGUIMode) throw new InvalidOperationException("Manufacturer Part Search requires Altium's graphical interface.");
            var currentDocument = AltiumApi.GlobalVars.Client.GetCurrentView()?.GetOwnerDocument();
            try
            {
                bool canPlace = string.Equals(currentDocument?.GetKind(), "SCH", StringComparison.OrdinalIgnoreCase);
                var window = new PartSearch.PartSearchWindow(canPlace);
                if (DialogHost.Show(window) != true || window.SelectedPart == null || window.SelectedModel == null) return;
                var imported = PartSearch.SupplierLibraryImporter.Import(window.SelectedPart, window.SelectedOffer, window.SelectedModel);
                if (window.PlaceInSchematic)
                {
                    if (!canPlace) throw new InvalidOperationException("Open a schematic before placing a component.");
                    AltiumApi.GlobalVars.Client.ShowDocument(currentDocument);
                    var manager = EDP.Utils.LoadIntegratedLibraryManager()
                        ?? throw new InvalidOperationException("Altium's library manager is unavailable.");
                    // The library manager requires explicit placement coordinates.
                    string placement = "Location.X=0|Location.Y=0|Orientation=0|PartID=" +
                        imported.PartId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (!manager.PlaceLibraryComponent(imported.Reference, imported.LibraryPath, placement))
                        throw new InvalidOperationException("Altium could not place the component. The imported component remains in ManufacturerParts.schlib.");
                    AltiumApi.GlobalVars.SCHServer.GetCurrentSchDocument()?.GraphicallyInvalidate();
                }
            }
            finally { if (currentDocument != null) AltiumApi.GlobalVars.Client.ShowDocument(currentDocument); }
        }
    }
}
