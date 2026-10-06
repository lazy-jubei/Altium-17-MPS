using SCH;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Altium17PartSearch.PartSearch
{
    internal sealed class SupplierOffer
    {
        public string Supplier { get; set; }
        public string Sku { get; set; }
        public int? Stock { get; set; }
        public string StockDisplay => Stock.HasValue ? Stock.Value.ToString("N0", CultureInfo.CurrentCulture) : "Unknown";
        public string Currency { get; set; }
        public string Url { get; set; }
        public string Updated { get; set; }
        public List<PriceBreak> Prices { get; } = new List<PriceBreak>();
        public string PriceSummary => string.Join("; ", Prices.Select(p => $"{p.Quantity}+: {p.UnitPrice} {Currency}"));
    }

    internal sealed class PriceBreak
    {
        public int Quantity { get; set; }
        // Keep the provider's formatted price rather than guessing its locale.
        public string UnitPrice { get; set; }
    }

    internal sealed class SupplierPart
    {
        public string Manufacturer { get; set; }
        public string Mpn { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public string Provider { get; set; }
        public string PartId { get; set; }
        public string Datasheet { get; set; }
        public DateTime RetrievedUtc { get; } = DateTime.UtcNow;
        public Dictionary<string, string> Parameters { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<SupplierOffer> Offers { get; } = new List<SupplierOffer>();
        public string Suppliers => string.Join(", ", Offers.Select(o => o.Supplier).Distinct());
        public string Stock => Offers.Any(o => o.Stock.HasValue)
            ? string.Join("; ", Offers.Where(o => o.Stock.HasValue).Select(o => $"{o.Supplier}: {o.Stock:N0}")) : "Unknown";

        public Dictionary<string, string> GetImportParameters(SupplierOffer preferred)
        {
            var result = new Dictionary<string, string>(Parameters, StringComparer.OrdinalIgnoreCase);
            result["Manufacturer"] = Manufacturer ?? "";
            result["Manufacturer Part Number"] = Mpn ?? "";
            result["Manufacturer 1"] = Manufacturer ?? "";
            result["Manufacturer Part Number 1"] = Mpn ?? "";
            result["Part Search Provider"] = Provider ?? "";
            result["Part Search Retrieved UTC"] = RetrievedUtc.ToString("u", CultureInfo.InvariantCulture);
            if (WebLinks.IsHttp(Datasheet))
            {
                result["Datasheet"] = Datasheet;
                result["ComponentLink1Description"] = "Datasheet";
                result["ComponentLink1URL"] = Datasheet;
            }
            if (preferred != null)
            {
                result["Supplier 1"] = preferred.Supplier ?? "";
                result["Supplier Part Number 1"] = preferred.Sku ?? "";
                if (WebLinks.IsHttp(preferred.Url)) result["Supplier URL 1"] = preferred.Url;
                if (preferred.Stock.HasValue) result["Supplier Stock Snapshot 1"] = preferred.Stock.Value.ToString(CultureInfo.InvariantCulture);
                if (preferred.Prices.Count > 0) result["Supplier Price Breaks Snapshot 1"] = preferred.PriceSummary;
            }
            return result;
        }

        public static SupplierPart Read(ISupplierSourceRelationship source, ISupplier provider, ISupplierManager manager)
        {
            var part = new SupplierPart
            {
                Manufacturer = source.GetState_ManufacturerName()?.Trim(), Mpn = source.GetState_ManufacturerPartNumber()?.Trim(),
                Description = source.GetState_Description(), Category = source.GetState_Category(),
                Provider = source.GetState_ProviderName(), PartId = source.GetState_PartId()
            };
            if (string.IsNullOrWhiteSpace(part.Provider)) part.Provider = provider.SourceName();
            for (int i = 0; i < source.GetState_ParametersCount(); i++)
            {
                var parameter = source.GetState_ParameterAt(i);
                if (parameter == null || string.IsNullOrWhiteSpace(parameter.GetState_Name())) continue;
                string value = parameter.GetState_Value() ?? "";
                string unit = parameter.GetState_Unit()?.Trim();
                if (!string.IsNullOrWhiteSpace(unit) && !string.IsNullOrWhiteSpace(value) && !value.TrimEnd().EndsWith(unit, StringComparison.Ordinal))
                    value = value.TrimEnd() + " " + unit;
                part.Parameters[parameter.GetState_Name()] = value;
            }
            string rohs = source.GetState_RoHS();
            if (!string.IsNullOrWhiteSpace(rohs)) part.Parameters["RoHS"] = rohs;
            part.Datasheet = part.Parameters.Where(p => p.Key.IndexOf("datasheet", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(p => p.Value).FirstOrDefault(WebLinks.IsHttp);
            // Supporting-document IDs are not necessarily URLs or datasheets.
            // Resolve neither by inventing a Content Vault download endpoint.
            int stock = source.GetState_QuantityInStock();
            var offer = new SupplierOffer
            {
                Supplier = source.GetState_SupplierName(), Sku = source.GetState_SupplierPartNumber(),
                Currency = source.GetState_Currency(), Stock = stock < 0 ? (int?)null : stock
            };
            // Ciiva's URL builder performs another supplier search and throws
            // when that lookup returns no part. Use links in its result instead.
            offer.Url = new[] { "Supplier URL", "Product URL", "Ciiva Url" }
                .Where(part.Parameters.ContainsKey).Select(key => part.Parameters[key]).FirstOrDefault(WebLinks.IsHttp);
            if (offer.Url == null && !string.Equals(part.Provider, "Ciiva", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(offer.Sku))
            {
                try
                {
                    string supplierSource = source.GetState_SupplierSource();
                    var linkProvider = string.IsNullOrWhiteSpace(supplierSource) ? provider : manager.GetSourceByName(supplierSource) ?? provider;
                    offer.Url = linkProvider.GetSearchUrl(offer.Sku);
                }
                catch (System.Runtime.InteropServices.COMException) { }
                catch (NullReferenceException) { }
            }
            // Providers implementing the original interface need not expose the newer timestamp.
            try { offer.Updated = (source as ISupplierSourceRelationship2)?.GetState_LastUpdated(); }
            catch (System.Runtime.InteropServices.COMException) { }
            for (int i = 0; i < source.GetState_UnitPriceCount(); i++)
                offer.Prices.Add(new PriceBreak { Quantity = source.GetState_BreakQuantity(i), UnitPrice = source.GetState_UnitPrice(i) });
            part.Offers.Add(offer);
            return part;
        }
    }

    internal static class WebLinks
    {
        internal static bool IsHttp(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }
}
