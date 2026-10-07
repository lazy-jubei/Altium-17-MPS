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
        private int? _stock;
        public int? Stock { get => _stock; set => _stock = value >= 0 ? value : null; }
        public string StockDisplay => Stock.HasValue ? Stock.Value.ToString("N0", CultureInfo.CurrentCulture) : "Unknown";
        public string Currency { get; set; }
        public string Url { get; set; }
        public string Updated { get; set; }
        public List<PriceBreak> Prices { get; } = new List<PriceBreak>();
        public string PriceSummary => string.Join("; ", Prices.OrderBy(p => p.Quantity).Select(p => $"{p.Quantity:N0}+: {p.UnitPrice} {Currency}"));

        internal PriceBreak PriceAt(int quantity) => Prices.Where(p => p.Quantity > 0 && p.Quantity <= quantity && !string.IsNullOrWhiteSpace(p.UnitPrice))
            .OrderByDescending(p => p.Quantity).FirstOrDefault();
    }

    internal sealed class PriceBreak
    {
        public int Quantity { get; set; }
        // Preserve provider formatting; prices can use different locales and currencies.
        public string UnitPrice { get; set; }
    }

    internal sealed class SupplierOfferRow
    {
        internal SupplierOffer Offer { get; }
        public string Supplier => Offer.Supplier;
        public int? Stock => Offer.Stock;
        public string StockDisplay => Offer.StockDisplay;
        public bool? CanSupply { get; }
        public string Availability { get; }
        public string UnitPrice { get; }

        internal SupplierOfferRow(SupplierOffer offer, int quantity)
        {
            if (quantity < 1) throw new ArgumentOutOfRangeException(nameof(quantity));
            Offer = offer;
            CanSupply = Stock.HasValue ? Stock.Value >= quantity : (bool?)null;
            Availability = !Stock.HasValue ? "Unknown" : Stock == 0 ? "Out of stock" : CanSupply == true ? "In stock" : $"Short by {quantity - Stock.Value:N0}";
            var price = offer.PriceAt(quantity);
            int? firstBreak = offer.Prices.Where(p => p.Quantity > 0 && !string.IsNullOrWhiteSpace(p.UnitPrice))
                .Select(p => (int?)p.Quantity).Min();
            UnitPrice = price != null ? (price.UnitPrice + " " + offer.Currency).Trim() : firstBreak.HasValue ? $"From {firstBreak:N0}+" : "—";
        }
    }

    internal static class SupplierAvailability
    {
        internal static bool SameListing(SupplierOffer a, SupplierOffer b) =>
            !string.IsNullOrWhiteSpace(a.Supplier) && !string.IsNullOrWhiteSpace(a.Sku) &&
            string.Equals(a.Supplier.Trim(), b.Supplier?.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.Sku.Trim(), b.Sku?.Trim(), StringComparison.OrdinalIgnoreCase);

        internal static void Merge(List<SupplierOffer> offers, IEnumerable<SupplierOffer> incoming)
        {
            foreach (var offer in incoming)
            {
                var previous = offers.FirstOrDefault(o => SameListing(o, offer));
                if (previous == null) { offers.Add(offer); continue; }
                if (offer.Stock.HasValue) previous.Stock = offer.Stock;
                if (!string.IsNullOrWhiteSpace(offer.Url)) previous.Url = offer.Url;
                if (!string.IsNullOrWhiteSpace(offer.Updated)) previous.Updated = offer.Updated;
                if (offer.Prices.Count > 0)
                {
                    previous.Currency = offer.Currency;
                    previous.Prices.Clear(); previous.Prices.AddRange(offer.Prices);
                }
            }
        }

        internal static string Summary(IReadOnlyCollection<SupplierOffer> offers, int quantity)
        {
            if (offers.Count == 0) return "No distributor data supplied.";
            var stocked = offers.Where(o => o.Stock >= quantity).ToList();
            int count = stocked.Where(o => !string.IsNullOrWhiteSpace(o.Supplier))
                .Select(o => o.Supplier.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            string summary = count > 0 ? $"{count} distributor{(count == 1 ? " reports" : "s report")} {quantity:N0}+ in stock."
                : stocked.Count > 0 ? $"{stocked.Count} listing{(stocked.Count == 1 ? " reports" : "s report")} {quantity:N0}+ in stock."
                : offers.Any(o => o.Stock > 0) ? $"No reported listing covers {quantity:N0}." : "No reported stock.";
            int unknown = offers.Count(o => !o.Stock.HasValue);
            return summary + (unknown == 0 ? "" : $" Stock unknown for {unknown} listing{(unknown == 1 ? "" : "s")}.");
        }
    }
}
