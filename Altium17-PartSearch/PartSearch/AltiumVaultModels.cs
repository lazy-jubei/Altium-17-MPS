using DXP;
using SCH;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Altium17PartSearch.PartSearch
{
    internal sealed class AltiumVaultModels
    {
        internal sealed class Model
        {
            internal IEDMS_Vault Vault;
            internal IULB_ItemRevision Revision;
            internal string Mpn, Manufacturer;
            public string VaultName => Vault.GetHRID();
            public string Reference => Revision.GetHRID();
        }

        private readonly TEDMS_VaultFlagSet _flags = new TEDMS_VaultFlagSet();

        internal async Task<List<Model>> FindAsync(SupplierPart part, CancellationToken token, Action<string> progress)
        {
            var manager = EDP.Utils.GetVaultManager()
                ?? throw new InvalidOperationException("Altium's Vault manager is unavailable.");
            var matches = new List<Model>();
            var errors = new List<string>();
            int searched = 0;
            for (int i = 0; i < manager.GetInstalledVaultCount(); i++)
            {
                token.ThrowIfCancellationRequested();
                var vault = manager.GetInstalledVault(i);
                if (vault == null || !vault.GetEnabled() || vault.GetIsHidden()) continue;
                string name = vault.GetHRID();
                progress("Looking for CAD models in " + name + "...");
                string phase = "connecting";
                try
                {
                    var connection = vault.CheckConnection(_flags, null, null);
                    await WaitAsync(connection, token);
                    Check(connection.GetResult());
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var queryErrors = new List<string>();
                    int successfulQueries = 0;
                    // Text indexing and exact parameter lookup are separate Vault services.
                    // An unavailable index must not prevent an exact lookup from working.
                    foreach (string field in CloudModelPolicy.MpnFields.Take(3))
                    {
                        phase = "looking up " + field;
                        try
                        {
                            var ids = vault.GetItemRevisionGUIDsByParameter(field, part.Mpn, _flags, null, null);
                            await WaitAsync(ids, token);
                            Check(ids.GetResult());
                            successfulQueries++;
                            var guids = ids.GetGUIDs();
                            RuntimeDiagnostics.Trace(name + " / " + field + ": " + (guids?.GetCount() ?? 0) + " revisions");
                            if (guids != null) for (int n = 0; n < guids.GetCount(); n++) seen.Add(guids.GetItem(n));
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception error) { queryErrors.Add(phase + ": " + error.Message); }
                    }
                    if (seen.Count == 0)
                    {
                        phase = "searching";
                        try
                        {
                            var options = EDP.Utils.CreateEDMS_SearchOptions()
                                ?? throw new InvalidOperationException("Altium's Vault search is unavailable.");
                            options.SetObjectClassSet(new TEDM_ObjectClassSet(TEDM_ObjectClass.eObjectClass_ItemRevision));
                            options.SetPageSize(100); options.SetPageIndex(0);
                            var search = vault.SearchVault(CloudModelPolicy.QuoteQuery(part.Mpn), options, _flags, null, null);
                            await WaitAsync(search, token);
                            var results = search.GetResult();
                            Check(results);
                            successfulQueries++;
                            RuntimeDiagnostics.Trace(name + " / text search: " + results.GetSearchItemCount() + " revisions");
                            for (int n = 0; n < results.GetSearchItemCount(); n++) seen.Add(results.GetSearchItem(n).GetGUID());
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception error) { queryErrors.Add(phase + ": " + error.Message); }
                    }
                    if (seen.Count == 0)
                    {
                        // Legacy Vaults can query revision comments without the text index.
                        phase = "searching revision comments";
                        progress("Searching component revisions in " + name + "...");
                        try
                        {
                            var revisions = vault.SearchItemRevisions("", "", part.Mpn, "", "", "", "", "", 100, _flags, null, null);
                            await WaitAsync(revisions, token);
                            Check(revisions.GetResult());
                            successfulQueries++;
                            var list = revisions.GetItemRevisions();
                            RuntimeDiagnostics.Trace(name + " / revision comments: " + (list?.GetCount() ?? 0) + " revisions");
                            for (int n = 0; n < (list?.GetCount() ?? 0); n++) seen.Add(((IULB_ItemRevision)list.Get(n)).GetGUID());
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception error) { queryErrors.Add(phase + ": " + error.Message); }
                    }
                    if (successfulQueries == 0) throw new InvalidOperationException(string.Join("; ", queryErrors));
                    if (seen.Count == 0 && queryErrors.Count > 0) throw new InvalidOperationException("No parameter matches; " + string.Join("; ", queryErrors));
                    searched++;
                    if (seen.Count > 100) throw new InvalidOperationException("Too many CAD matches. Refine the MPN before downloading.");
                    foreach (string guid in seen)
                    {
                        phase = "reading component revisions";
                        token.ThrowIfCancellationRequested();
                        var revisions = vault.GetItemRevisionsByGUID(guid, _flags, null, null);
                        await WaitAsync(revisions, token);
                        Check(revisions.GetResult());
                        var revision = revisions.GetFirstItemRevision();
                        RuntimeDiagnostics.Trace(name + " / revision " + revision?.GetHRID());
                        if (revision == null || !revision.GetIsApplicable() || !revision.GetCanRead()) continue;
                        if (revision.GetReleaseDate() <= 0) continue;
                        // GetComponents can return no record for legacy Content Vault items.
                        // Match their parameters/part choices; validate the actual CAD component on download.
                        if (!Matches(revision, part) && !MatchesPartChoice(vault, revision, part)) continue;
                        matches.Add(new Model { Vault = vault, Revision = revision, Mpn = part.Mpn, Manufacturer = part.Manufacturer });
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error)
                {
                    RuntimeDiagnostics.Error("Vault CAD lookup: " + name + " (" + phase + ")", error);
                    errors.Add(name + " (" + phase + "): " + error.Message);
                }
            }
            if (matches.Count == 0 && errors.Count > 0)
                throw new InvalidOperationException("CAD lookup failed. " + string.Join("; ", errors));
            if (searched == 0)
                throw new InvalidOperationException("Connect to a Vault in Altium Preferences → Data Management → Vaults, then try again.");
            return matches;
        }

        internal async Task<LibraryModelChoice> DownloadAsync(Model model, CancellationToken token, Action<string> progress)
        {
            var cache = EDP.Utils.GetEDMS_VaultFileCache()
                ?? throw new InvalidOperationException("Altium's model cache is unavailable.");
            var manager = EDP.Utils.GetVaultManager();
            int count = 0;
            var cachedModels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var root = new CloudModelPolicy.Revision { VaultGuid = model.Vault.GetVaultGUID(), RevisionGuid = model.Revision.GetGUID() };
            await CloudModelPolicy.DownloadGraphAsync(root, async next =>
            {
                var vault = manager?.GetInstalledVaultByGUID(next.VaultGuid);
                if (vault == null || !vault.GetEnabled())
                    throw new InvalidOperationException("A linked model belongs to a Vault that is not connected.");
                progress($"Downloading CAD models ({++count})...");
                var data = vault.GetDataFilesForItemRevision(next.RevisionGuid,
                    new TULB_ReleasedFileTypeSet(TULB_ReleasedFileType.eReleasedFileType_Design, TULB_ReleasedFileType.eReleasedFileType_Released),
                    _flags, null, null);
                await WaitAsync(data, token);
                Check(data.GetResult());
                var folders = data.GetDataFolders();
                bool hasFiles = false;
                for (int i = 0; i < (folders?.GetCount() ?? 0); i++) hasFiles |= HasFiles((IULB_DataFolder)folders.Get(i), 0);
                RuntimeDiagnostics.Trace("Revision " + next.RevisionGuid + ": has CAD files=" + hasFiles);
                // Components can be metadata-only records; their child revisions hold the actual CAD files.
                if (hasFiles)
                {
                    var files = cache.GetCachedFilesForItemRevision(vault, next.RevisionGuid, _flags, null, null);
                    await WaitAsync(files, token);
                    var result = files.GetResult();
                    Check(result);
                    cachedModels[next.Key] = result.GetFilePath();
                    RuntimeDiagnostics.Trace("Cached revision " + next.RevisionGuid + ": " + result.GetFilePath());
                }
                var links = vault.GetItemRevisionLinks(next.RevisionGuid, "", _flags, null, null);
                await WaitAsync(links, token);
                Check(links.GetResult());
                var children = links.GetItemRevisionLinks();
                RuntimeDiagnostics.Trace("Linked revisions=" + children?.GetCount());
                var revisions = new List<CloudModelPolicy.Revision>();
                for (int i = 0; i < (children?.GetCount() ?? 0); i++)
                {
                    var link = children.Get(i) as IULB_ItemRevisionLink;
                    if (link == null || string.IsNullOrWhiteSpace(link.GetChildItemRevisionGUID())) continue;
                    string vaultGuid = link.GetChildVaultGUID();
                    revisions.Add(new CloudModelPolicy.Revision
                    {
                        VaultGuid = string.IsNullOrWhiteSpace(vaultGuid) ? next.VaultGuid : vaultGuid,
                        RevisionGuid = link.GetChildItemRevisionGUID()
                    });
                }
                return revisions;
            }, token);
            token.ThrowIfCancellationRequested();
            var choice = new LibraryModelChoice
            {
                Reference = model.Reference, LibraryPath = model.VaultName, PartId = 1,
                VaultName = model.VaultName, VaultGuid = model.Vault.GetVaultGUID(), RevisionGuid = model.Revision.GetGUID(),
                VerifiedMpn = model.Mpn, VerifiedManufacturer = model.Manufacturer, CachedModels = cachedModels
            };
            var loaded = choice.Load() ?? throw new InvalidOperationException("Altium could not load the downloaded component.");
            if (!Guid.TryParse(loaded.GetState_RevisionGUID(), out var loadedGuid) ||
                loadedGuid != Guid.Parse(choice.RevisionGuid))
                throw new InvalidOperationException("Altium loaded a different component revision. The import was stopped.");
            return choice;
        }

        private static bool Matches(IULB_ItemRevision revision, SupplierPart part) => CloudModelPolicy.Matches(
            CloudModelPolicy.FirstValue(CloudModelPolicy.MpnFields, revision.GetParameterByName) ?? revision.GetComment(),
            CloudModelPolicy.FirstValue(CloudModelPolicy.ManufacturerFields, revision.GetParameterByName), part.Mpn, part.Manufacturer);

        private static bool HasFiles(IULB_DataFolder folder, int depth)
        {
            if (depth > 20) throw new InvalidOperationException("The model file tree is too deep.");
            if (folder.GetFileCount() > 0) return true;
            for (int i = 0; i < folder.GetFolderCount(); i++) if (HasFiles(folder.GetFolder(i), depth + 1)) return true;
            return false;
        }

        private static bool MatchesPartChoice(IEDMS_Vault vault, IULB_ItemRevision revision, SupplierPart part)
        {
            var catalog = EDP.Utils.GetPartCatalog(vault.GetVaultGUID());
            var result = catalog?.GetItemPartChoiceList(revision.GetItemGUID());
            if (result == null || !result.GetIsSuccess()) return false;
            var choices = result.GetPartChoiceList();
            RuntimeDiagnostics.Trace("Part choices=" + choices?.GetCount());
            for (int i = 0; i < (choices?.GetCount() ?? 0); i++)
            {
                var choice = choices.GetPartChoice(i);
                RuntimeDiagnostics.Trace("Choice MPN=" + choice.GetMPN() + ", manufacturer=" + choice.GetManufacturerName());
                if (CloudModelPolicy.Matches(choice.GetMPN(), choice.GetManufacturerName(), part.Mpn, part.Manufacturer)) return true;
            }
            return false;
        }

        private static void Check(IEDM_Result result)
        {
            if (result == null) throw new InvalidOperationException("Altium returned no Vault result.");
            if (!result.GetSuccess()) throw new InvalidOperationException(result.GetMessage());
        }

        private static void Check(IEDM_RecordResultList records)
        {
            // SDK interfaces share the IDispatch IID. An "is" test against an unrelated
            // interface can succeed, then invoke a method the native object does not have.
            Check((IEDM_Result)records);
            if (records.GetFirstErrorIndex() >= 0)
                throw new InvalidOperationException(records.GetFirstErrorMessage());
        }

        private static async Task WaitAsync(IEDM_AsyncResult request, CancellationToken token)
        {
            if (request == null) throw new InvalidOperationException("Altium could not start the Vault request.");
            // Stay on Altium's UI thread while giving its native async worker time to finish.
            await CloudModelPolicy.WaitAsync(request.IsDone, token, TimeSpan.FromSeconds(30));
        }
    }
}
