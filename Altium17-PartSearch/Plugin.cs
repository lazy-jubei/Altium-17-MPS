using DXP;
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
        private readonly PartSearch.PartSearchPanelView panel;
        public ManufacturerPartSearchModule(IClient client) : base(client, "Altium17-PartSearch")
        {
            noGUIMode = client.ProductInfo().SupportsUIFeature("NoGUI", false);
            if (!noGUIMode)
            {
                panel = new PartSearch.PartSearchPanelView();
                AddView(panel);
                client.AddServerView(panel);
            }
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

        internal const string PanelName = "Altium17Mps";
        protected override IServerView CreateServerViewImpl(string name)
        {
            if (!string.Equals(name, PanelName, StringComparison.OrdinalIgnoreCase)) return base.CreateServerViewImpl(name);
            return panel;
        }
        private void RunManufacturerPartSearch(IServerDocumentView context, ref string parameters)
        {
            if (noGUIMode) throw new InvalidOperationException("Manufacturer Part Search requires Altium's graphical interface.");
            RuntimeDiagnostics.Trace("Opening Manufacturer Part Search panel");
            var gui = AltiumApi.GlobalVars.Client.GetGUIManager();
            gui.SetPanelVisibleInCurrentForm(PanelName, true);
            gui.SetPanelActiveInCurrentForm(PanelName);
        }
    }
}
