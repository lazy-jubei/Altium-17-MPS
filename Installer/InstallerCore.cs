using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Altium17MpsSetup
{
    internal static class InstallerCore
    {
        internal const string Id = "Altium17-PartSearch";
        internal static readonly string[] Files = { Id + ".dll", Id + ".Ins", Id + ".rcs", Id + ".target.json" };

        internal static string Install(string root, IDictionary<string, byte[]> payload, string version,
            Action<string, string, string> commitRegistry = null)
        {
            if (payload.Count != Files.Length || Files.Any(name => !payload.TryGetValue(name, out var bytes) || bytes.Length == 0))
                throw new InvalidOperationException("The installer payload is incomplete.");
            root = Path.GetFullPath(root);
            string registry = Path.Combine(root, "ExtensionsRegistry.xml");
            var xml = XDocument.Load(registry, LoadOptions.PreserveWhitespace);
            if (xml.Root?.Name != "Extensions") throw new InvalidOperationException("Invalid Altium extension registry.");
            var matches = xml.Root.Elements("Item").Where(x => (string)x.Attribute("HRID") == Id).ToList();
            if (matches.Count > 1) throw new InvalidOperationException("Duplicate MPS entries in Altium's extension registry.");
            string target = Path.Combine(root, Id);
            var item = matches.SingleOrDefault();
            if (item == null)
            {
                item = new XElement("Item", new XAttribute("HRID", Id), new XAttribute("Guid", "899CC555-282B-48F0-962C-AA3ED1C88164"));
                foreach (var field in new Dictionary<string, string> {
                    ["Status"] = "0", ["VaultGuid"] = "", ["CreatedBy"] = "lazy-jubei",
                    ["CategoryGuid"] = "793A1F67-0B22-4E01-A5DE-3176A1E8C60D", ["CategoryName"] = "",
                    ["ReadMe"] = "", ["Help"] = "", ["Requirements"] = "", ["SmallImage"] = "", ["LargeImage"] = "",
                    ["Title"] = "Altium 17 MPS", ["ShortDescription"] = "Manufacturer Part Search",
                    ["LongDescription"] = "Manufacturer Part Search backport for Altium Designer 17",
                    ["VersionGuid"] = "EFB93D5A-B0CF-4A4A-9B66-B04A142C49AE", ["ReleaseNotes"] = "" })
                    item.Add(new XElement(field.Key, field.Value));
                xml.Root.Add(item);
            }
            item.SetElementValue("Path", target);
            item.SetElementValue("Version", version);
            string date = DateTime.Now.ToOADate().ToString("F7", CultureInfo.InvariantCulture);
            item.SetElementValue("DateInstalled", date);
            item.SetElementValue("ReleasedDate", date);
            var platforms = item.Element("PlatformVersions");
            if (platforms == null) { platforms = new XElement("PlatformVersions"); item.Add(platforms); }
            foreach (var platform in new Dictionary<string, string> {
                ["DXP"] = "1.0.5.5", ["EDP"] = "10.0.5.5", ["MaxDXP"] = "0.0.0.0", ["MaxEDP"] = "0.0.0.0" })
            {
                var element = platforms.Element(platform.Key);
                if (element == null) { element = new XElement(platform.Key); platforms.Add(element); }
                element.SetAttributeValue("BuildNumber", platform.Value);
            }

            string stamp = Guid.NewGuid().ToString("N"), stage = Path.Combine(root, "." + Id + "." + stamp);
            string backup = Path.Combine(root, Id + "." + stamp + ".backup"), temporary = registry + "." + stamp + ".tmp";
            bool replacedPlugin = false, movedOriginal = false;
            try
            {
                Directory.CreateDirectory(stage);
                foreach (string name in Files) File.WriteAllBytes(Path.Combine(stage, name), payload[name]);
                // AD17's extension loader expects UTF-8 without a byte-order mark.
                using (var writer = XmlWriter.Create(temporary, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
                    xml.Save(writer);
                Directory.CreateDirectory(backup);
                File.Copy(registry, Path.Combine(backup, "ExtensionsRegistry.xml"));
                if (Directory.Exists(target))
                {
                    Directory.Move(target, Path.Combine(backup, Id));
                    movedOriginal = true;
                }
                Directory.Move(stage, target);
                replacedPlugin = true;
                (commitRegistry ?? File.Replace)(temporary, registry, null);
                return backup;
            }
            catch
            {
                if (replacedPlugin) Directory.Delete(target, true);
                if (movedOriginal) Directory.Move(Path.Combine(backup, Id), target);
                if (replacedPlugin || movedOriginal) File.Copy(Path.Combine(backup, "ExtensionsRegistry.xml"), registry, true);
                throw;
            }
            finally
            {
                if (Directory.Exists(stage)) Directory.Delete(stage, true);
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
