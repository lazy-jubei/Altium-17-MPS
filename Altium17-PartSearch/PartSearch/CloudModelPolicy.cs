using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Altium17PartSearch.PartSearch
{
    internal static class CloudModelPolicy
    {
        internal static readonly string[] MpnFields = { "Manufacturer Part Number", "Manufacturer Part Number 1", "MPN", "Part Number" };
        internal static readonly string[] ManufacturerFields = { "Manufacturer", "Manufacturer 1", "Manufacturer Name" };

        internal static bool Matches(string mpn, string manufacturer, string wantedMpn, string wantedManufacturer)
        {
            // Preserve suffixes, separators and package codes: a fuzzy match can select a different device.
            return !string.IsNullOrWhiteSpace(mpn) &&
                string.Equals(mpn.Trim(), wantedMpn?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(manufacturer) &&
                string.Equals(ManufacturerName(manufacturer), ManufacturerName(wantedManufacturer), StringComparison.OrdinalIgnoreCase);
        }

        private static string ManufacturerName(string name)
        {
            name = name?.Trim();
            // Ciiva combines this FPGA brand with its former parent; legacy Vault metadata uses Altera.
            return string.Equals(name, "Intel / Altera", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Intel/Altera", StringComparison.OrdinalIgnoreCase) ? "Altera" : name;
        }

        internal static string FirstValue(IEnumerable<string> names, Func<string, string> read) =>
            names.Select(read).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        internal static string QuoteQuery(string mpn) => "\"" + mpn.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        internal static string FindModelFile(string cachedPath, string name, string type)
        {
            string extension = "." + type;
            if (File.Exists(cachedPath) && string.Equals(Path.GetExtension(cachedPath), extension, StringComparison.OrdinalIgnoreCase))
                return cachedPath;
            if (!Directory.Exists(cachedPath)) throw new InvalidOperationException("The downloaded model cache is missing.");
            var files = Directory.EnumerateFiles(cachedPath, "*", SearchOption.AllDirectories)
                .Where(p => string.Equals(Path.GetExtension(p), extension, StringComparison.OrdinalIgnoreCase)).ToList();
            var named = files.Where(p => string.Equals(Path.GetFileNameWithoutExtension(p), name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (named.Count > 0) files = named;
            var released = files.Where(p => string.Equals(new DirectoryInfo(Path.GetDirectoryName(p)).Name, "Released", StringComparison.OrdinalIgnoreCase)).ToList();
            if (released.Count > 0) files = released;
            if (files.Count != 1) throw new InvalidOperationException("Could not identify one downloaded library for model '" + name + "'.");
            return files[0];
        }

        internal sealed class Revision
        {
            internal string VaultGuid, RevisionGuid;
            internal string Key
            {
                get
                {
                    if (!Guid.TryParse(VaultGuid, out var vault) || !Guid.TryParse(RevisionGuid, out var revision))
                        throw new InvalidOperationException("The Vault returned an invalid model revision ID.");
                    return vault.ToString("D") + "/" + revision.ToString("D");
                }
            }
        }

        internal static async Task DownloadGraphAsync(Revision root,
            Func<Revision, Task<IEnumerable<Revision>>> download, CancellationToken token)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root.Key };
            var pending = new Queue<Revision>();
            pending.Enqueue(root);
            while (pending.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                var children = await download(pending.Dequeue());
                token.ThrowIfCancellationRequested();
                foreach (var child in children)
                {
                    if (!seen.Add(child.Key)) continue;
                    if (seen.Count > 100) throw new InvalidOperationException("The component has too many linked model revisions.");
                    pending.Enqueue(child);
                }
            }
        }

        internal static async Task WaitAsync(Func<bool> done, CancellationToken token, TimeSpan timeout)
        {
            var elapsed = Stopwatch.StartNew();
            while (!done())
            {
                token.ThrowIfCancellationRequested();
                if (elapsed.Elapsed > timeout)
                    throw new TimeoutException("The Vault did not respond. Check its connection in Altium Preferences.");
                await Task.Delay(100, token);
            }
            token.ThrowIfCancellationRequested();
        }
    }
}
