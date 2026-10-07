using DXP;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

namespace Altium17PartSearch.PartSearch
{
    internal sealed class PartSearchPanelView : ServerPanelView
    {
        internal PartSearchPanelView() : base(new PartSearchPanel(), ManufacturerPartSearchModule.PanelName, "Manufacturer Part Search (AD17)") { }
    }

    internal sealed class PartSearchPanel : ServerPanelForm
    {
        private readonly PartSearchView view = new PartSearchView();
        internal PartSearchPanel()
        {
            Text = "Manufacturer Part Search (AD17)";
            ClientSize = new Size(620, 840);
            MinimumSize = new Size(360, 350);
            Controls.Add(new ElementHost { Dock = DockStyle.Fill, Child = view });
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) view?.Dispose();
            base.Dispose(disposing);
        }
    }
}
