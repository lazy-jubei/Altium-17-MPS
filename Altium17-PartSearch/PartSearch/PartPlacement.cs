using DXP;
using System;

namespace Altium17PartSearch.PartSearch
{
    internal static class PartPlacement
    {
        internal static IServerDocumentView CurrentSchematic()
        {
            var view = AltiumApi.GlobalVars.Client.GetCurrentView();
            return string.Equals(view?.GetOwnerDocument()?.GetKind(), "SCH", StringComparison.OrdinalIgnoreCase) ? view : null;
        }
        internal static bool CanPlace => CurrentSchematic() != null;
        internal static void Place(IServerDocumentView context, SupplierLibraryImporter.ImportedComponent component)
        {
            var current = CurrentSchematic();
            if (current == null || !string.Equals(current.GetOwnerDocument().GetFileName(), context.GetOwnerDocument().GetFileName(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The active schematic changed. Select the intended sheet and click Place part again.");
            var client = AltiumApi.GlobalVars.Client;
            client.ShowDocument(current.GetOwnerDocument());
            // This is AD17's own interactive library placement command; omitting
            // Location.X/Y attaches the symbol to the cursor instead of the origin.
            string parameters = "LibReference=" + component.Reference + "|Library=" + component.LibraryPath + "|PartID=" + component.PartId;
            DXP.Utils.RunCommand("IntegratedLibrary:PlaceLibraryComponent", parameters, current);
        }
    }
}
