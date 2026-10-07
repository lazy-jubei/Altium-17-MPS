using DXP;
using SCH;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Altium17PartSearch.PartSearch
{
    internal sealed class LibraryModelChoice
    {
        internal string Reference { get; set; }
        internal string LibraryPath { get; set; }
        internal int PartId { get; set; }
        internal string FootprintName { get; set; }
        internal string VaultName { get; set; }
        internal string VaultGuid { get; set; }
        internal string RevisionGuid { get; set; }
        internal string VerifiedMpn { get; set; }
        internal string VerifiedManufacturer { get; set; }
        internal Dictionary<string, string> CachedModels { get; set; }

        private ISch_Component _component;
        private ISch_Component LoadCopy() => string.IsNullOrWhiteSpace(VaultName)
            ? AltiumApi.GlobalVars.SCHServer.LoadComponentFromLibrary(Reference, LibraryPath)
            : AltiumApi.GlobalVars.SCHServer.LoadComponent(EDP.TLibIdentifierKind.eLibIdentifierKind_VaultName, VaultName, Reference);
        internal ISch_Component Load() => _component ??= LoadCopy();
        internal ISch_Component LoadForImport() => LoadCopy();

        internal static LibraryModelChoice Browse(string suggestedMpn)
        {
            var manager = EDP.Utils.LoadIntegratedLibraryManager()
                ?? throw new InvalidOperationException("Altium's library manager is unavailable.");
            string reference = suggestedMpn ?? "", schLibrary = "", model = "", modelLibrary = "", library = "", modelType = "";
            int partId = 1;
            manager.BrowseForComponent(ref reference, ref schLibrary, ref model, ref modelLibrary,
                ref library, ref modelType);
            if (string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(schLibrary)) return null;
            return new LibraryModelChoice { Reference = reference, LibraryPath = schLibrary, PartId = partId };
        }
    }

    internal static class SupplierLibraryImporter
    {
        internal sealed class ImportedComponent
        {
            internal string LibraryPath { get; set; }
            internal string Reference { get; set; }
            internal int PartId { get; set; }
        }

        internal static ImportedComponent Import(SupplierPart part, SupplierOffer offer, LibraryModelChoice choice)
        {
            // Clone a component through Altium's own library loader. Its symbol,
            // footprint/model links and multipart definition stay together.
            var component = choice.LoadForImport()
                ?? throw new InvalidOperationException("Altium could not load the chosen library component.");
            var parameters = GetParameters(component);
            bool cloudMatch = !string.IsNullOrWhiteSpace(choice.VaultName) &&
                CloudModelPolicy.Matches(choice.VerifiedMpn, choice.VerifiedManufacturer, part.Mpn, part.Manufacturer);
            if (!string.IsNullOrWhiteSpace(choice.VaultName) && !cloudMatch)
                throw new InvalidOperationException("The downloaded CAD model was matched to a different manufacturer part.");
            string modelMpn = parameters.Where(p => string.Equals(p.Key, "Manufacturer Part Number", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.Key, "Manufacturer Part Number 1", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Value.GetState_Text()).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            if (!cloudMatch && !string.IsNullOrWhiteSpace(modelMpn) && !string.Equals(modelMpn.Trim(), part.Mpn?.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"The library component declares MPN '{modelMpn}', but the selected part is '{part.Mpn}'. Choose a matching model.");
            string modelManufacturer = parameters.Where(p => string.Equals(p.Key, "Manufacturer", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.Key, "Manufacturer 1", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Value.GetState_Text()).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            // An exact native part choice can supersede stale manufacturer/MPN labels in a shared symbol.
            if (!cloudMatch && !string.IsNullOrWhiteSpace(modelManufacturer) && !string.IsNullOrWhiteSpace(part.Manufacturer) &&
                !string.Equals(modelManufacturer.Trim(), part.Manufacturer.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"The library component declares manufacturer '{modelManufacturer}', but the selected part is from '{part.Manufacturer}'. Choose a matching model.");
            MakeModelLinksExplicit(component, choice);

            string root = Environment.GetEnvironmentVariable("ALTIUM_PART_SEARCH_LIBRARY_DIR")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AltiumParts");
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "ManufacturerParts.schlib");
            var original = AltiumApi.GlobalVars.Client.GetCurrentView()?.GetOwnerDocument();
            IServerDocument libraryDocument = null;
            try
            {
                libraryDocument = AltiumApi.GlobalVars.Client.OpenDocument("SchLib", path)
                    ?? throw new InvalidOperationException("Could not open the manufacturer parts library.");
                AltiumApi.GlobalVars.Client.ShowDocument(libraryDocument);
                var library = EESCH.GetCurrentSchLibrary()
                    ?? throw new InvalidOperationException("Could not access the manufacturer parts library.");
                string baseName = "MPS_" + (part.Manufacturer + "_" + part.Mpn).Trim('_');
                baseName = string.Concat(baseName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                string reference = baseName;
                for (int suffix = 2; library.GetState_SchComponentByLibRef(reference) != null; suffix++) reference = baseName + "_" + suffix;
                var process = AltiumApi.GlobalVars.Client.GetProcessControl();
                process.PreProcess(libraryDocument, "");
                try
                {
                    component.SetState_LibReference(reference);
                    component.SetState_SourceLibraryName(path);
                    component.SetState_DesignItemId(reference);
                    component.SetState_SymbolReference(reference);
                    // This is a local copy, not an edited revision of the source Vault item.
                    component.SetState_VaultGUID(""); component.SetState_ItemGUID(""); component.SetState_RevisionGUID("");
                    component.SetState_SymbolVaultGUID(""); component.SetState_SymbolItemGUID(""); component.SetState_SymbolRevisionGUID("");
                    component.SetState_ComponentDescription(part.Description ?? "");
                    component.SetState_CurrentPartID(choice.PartId);
                    var values = part.GetImportParameters(offer);
                    values["Part Search Source Library"] = choice.LibraryPath;
                    values["Part Search Source Reference"] = choice.Reference;
                    if (!string.IsNullOrWhiteSpace(choice.VaultGuid))
                    {
                        values["Part Search Source Vault GUID"] = choice.VaultGuid;
                        values["Part Search Source Revision GUID"] = choice.RevisionGuid;
                        values["CAD Source Manufacturer"] = modelManufacturer;
                        values["CAD Source MPN"] = modelMpn;
                    }
                    foreach (var value in values.Where(p => !string.IsNullOrWhiteSpace(p.Value)))
                    {
                        if (parameters.TryGetValue(value.Key, out var parameter)) parameter.SetState_Text(value.Value);
                        else EESCH.AddParameter(component, value.Key, value.Value);
                    }
                    library.AddSchComponent(component);
                    library.SetState_Current_SchComponent(component);
                    library.GraphicallyInvalidate();
                }
                finally { process.PostProcess(libraryDocument, ""); }
                libraryDocument.SetModified(true);
                // DoFileSave takes the editor's save-filter name, not its file extension.
                if (!libraryDocument.DoFileSave("Advanced Schematic binary library"))
                    throw new InvalidOperationException("Could not save the manufacturer parts library.");
                return new ImportedComponent { LibraryPath = path, Reference = reference, PartId = choice.PartId };
            }
            finally
            {
                if (original != null) AltiumApi.GlobalVars.Client.ShowDocument(original);
                // Leave the imported library open so its symbol and model links can be reviewed.
            }
        }

        private static void MakeModelLinksExplicit(ISch_Component component, LibraryModelChoice choice)
        {
            var manager = EDP.Utils.LoadIntegratedLibraryManager()
                ?? throw new InvalidOperationException("Altium's library manager is unavailable.");
            var iterator = component.SchIterator_Create();
            if (iterator == null) throw new InvalidOperationException("Could not inspect the chosen component's models.");
            try
            {
                iterator.AddFilter_ObjectSet(new TObjectSet(TObjectId.eImplementation));
                for (var item = iterator.FirstSchObject(); item != null; item = iterator.NextSchObject())
                {
                    var model = (ISch_Implementation)item;
                    if (!string.IsNullOrEmpty(choice.FootprintName) && string.Equals(model.GetState_ModelType(), "PCBLIB", StringComparison.OrdinalIgnoreCase))
                        model.SetState_IsCurrent(string.Equals(model.GetState_ModelName(), choice.FootprintName, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrWhiteSpace(model.GetState_ModelVaultGUID()))
                    {
                        if (choice.CachedModels == null) continue;
                        string key = new CloudModelPolicy.Revision { VaultGuid = model.GetState_ModelVaultGUID(),
                            RevisionGuid = model.GetState_ModelRevisionGUID() }.Key;
                        if (!choice.CachedModels.TryGetValue(key, out var cachedPath))
                            throw new InvalidOperationException("The component's model revision was not downloaded.");
                        string location = CloudModelPolicy.FindModelFile(cachedPath, model.GetState_ModelName(), model.GetState_ModelType());
                        RuntimeDiagnostics.Trace("Local model " + model.GetState_ModelName() + ": " + location);
                        // A local symbol has no Vault identity. Its footprint must point to the cached library explicitly.
                        model.SetState_DatalinksLocked(false);
                        model.SetState_DatabaseDatalinksLocked(false);
                        model.ClearAllDatafileLinks();
                        model.AddDataFileLink(model.GetState_ModelName(), location, model.GetState_ModelType());
                        model.SetState_UseComponentLibrary(false);
                        model.SetState_ModelVaultGUID(""); model.SetState_ModelItemGUID(""); model.SetState_ModelRevisionGUID("");
                        continue;
                    }
                    if (!model.GetState_UseComponentLibrary()) continue;
                    int count = model.GetState_DatafileLinkCount();
                    if (count == 0 && !string.Equals(model.GetState_ModelType(), "PCBLIB", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(model.GetState_ModelType(), "PCB3DLIB", StringComparison.OrdinalIgnoreCase))
                    {
                        // Built-in simulation/SI models can have no external datafiles.
                        model.SetState_UseComponentLibrary(false);
                        continue;
                    }
                    var locations = new List<string>();
                    for (int i = 0; i < Math.Max(count, 1); i++)
                    {
                        string foundIn = "";
                        string location = manager.GetComponentDatafileLocation(i, model.GetState_ModelName(), model.GetState_ModelType(),
                            choice.Reference, choice.LibraryPath, ref foundIn);
                        if (!File.Exists(location) && File.Exists(foundIn)) location = foundIn;
                        if (string.IsNullOrWhiteSpace(location) || !File.Exists(location))
                            throw new InvalidOperationException($"Altium could not resolve model '{model.GetState_ModelName()}'. Install its source library before copying this component.");
                        locations.Add(location);
                    }
                    if (count == 0) model.AddDataFileLink(model.GetState_ModelName(), locations[0], model.GetState_ModelType());
                    else for (int i = 0; i < count; i++) model.GetState_SchDatafileLink(i).SetState_Location(locations[i]);
                    model.SetState_UseComponentLibrary(false);
                }
            }
            finally { component.SchIterator_Destroy(ref iterator); }
        }

        private static Dictionary<string, ISch_Parameter> GetParameters(ISch_Component component)
        {
            var result = new Dictionary<string, ISch_Parameter>(StringComparer.OrdinalIgnoreCase);
            var iterator = component.SchIterator_Create();
            if (iterator == null) return result;
            try
            {
                iterator.AddFilter_ObjectSet(new TObjectSet(TObjectId.eParameter));
                for (var item = iterator.FirstSchObject(); item != null; item = iterator.NextSchObject())
                    if (item is ISch_Parameter parameter && !string.IsNullOrWhiteSpace(parameter.GetState_Name()))
                        result[parameter.GetState_Name()] = parameter;
            }
            finally { component.SchIterator_Destroy(ref iterator); }
            return result;
        }
    }
}
