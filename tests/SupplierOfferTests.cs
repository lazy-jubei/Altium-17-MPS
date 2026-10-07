using Altium17PartSearch.PartSearch;
using System.Globalization;

void Require(bool condition, string description) { if (!condition) throw new Exception(description); Console.WriteLine("PASS " + description); }
var offer = new SupplierOffer { Supplier = "DigiKey", Sku = "PART-ND", Stock = 250, Currency = "USD" };
offer.Prices.AddRange(new[] {
    new PriceBreak { Quantity = 100, UnitPrice = "0.15" },
    new PriceBreak { Quantity = 1, UnitPrice = "0.20" },
    new PriceBreak { Quantity = 1000, UnitPrice = "0.10" }
});
Require(new SupplierOfferRow(offer, 99).UnitPrice == "0.20 USD", "prices do not use a discount before its quantity threshold");
Require(new SupplierOfferRow(offer, 100).UnitPrice == "0.15 USD", "unordered breaks select the correct threshold");
Require(new SupplierOfferRow(offer, 250).CanSupply == true, "exact available quantity can be supplied");
Require(new SupplierOfferRow(offer, 300).Availability == "Short by 50", "insufficient stock shows the shortage");
Require(new SupplierOfferRow(offer, 1000).UnitPrice == "0.10 USD", "price breaks remain visible even when stock is insufficient");
var unknown = new SupplierOffer { Supplier = "Mouser", Sku = "OTHER", Stock = -1 };
Require(unknown.Stock == null && new SupplierOfferRow(unknown, 1).CanSupply == null, "negative stock is unknown, not zero or available");
unknown.Stock = 0;
Require(new SupplierOfferRow(unknown, 1).Availability == "Out of stock", "reported zero differs from unknown inventory");
Require(new SupplierOfferRow(unknown, int.MaxValue).UnitPrice == "—", "missing prices are not fabricated");
var reel = new SupplierOffer { Currency = "EUR" };
reel.Prices.Add(new PriceBreak { Quantity = 1000, UnitPrice = "0,012" });
Require(new SupplierOfferRow(reel, 1).UnitPrice.StartsWith("From "), "a reel price is not offered at quantities below its first price break");
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
Require(new SupplierOfferRow(reel, 1000).UnitPrice == "0,012 EUR", "provider locale and currency are preserved without guessing");
var offers = new List<SupplierOffer> { offer, unknown };
SupplierAvailability.Merge(offers, new[] { new SupplierOffer { Supplier = " digikey ", Sku = "part-nd", Stock = 300, Url = "https://example.com/part" } });
Require(offers.Count == 2 && offer.Stock == 300 && offer.Prices.Count == 3, "duplicate listings update stock without discarding existing price breaks");
SupplierAvailability.Merge(offers, new[] { new SupplierOffer { Supplier = "DIGIKEY", Sku = "PART-ND", Stock = null } });
Require(offer.Stock == 300, "an incomplete duplicate cannot erase known stock");
SupplierAvailability.Merge(offers, new[] { new SupplierOffer { Supplier = "DigiKey", Sku = "PART-REEL", Stock = 500 } });
Require(offers.Count == 3, "different distributor SKUs remain separate");
Require(SupplierAvailability.Summary(offers, 1).StartsWith("1 distributor report"), "multiple packaging listings do not inflate the distributor count");
Require(SupplierAvailability.Summary(offers, 400).StartsWith("1 distributor report"), "availability compares individual listings, without summing possibly shared stock");
Require(SupplierAvailability.Summary(offers, 600).StartsWith("No reported listing covers"), "separate listings are not added to falsely cover an order");
unknown.Stock = null;
Require(SupplierAvailability.Summary(offers, 600).Contains("Stock unknown for 1 listing."), "incomplete inventory coverage stays visible");
var unnamed = new List<SupplierOffer> { new SupplierOffer(), new SupplierOffer() };
SupplierAvailability.Merge(unnamed, new[] { new SupplierOffer() });
Require(unnamed.Count == 3, "listings with missing identity are not incorrectly deduplicated");
Require(SupplierAvailability.Summary(Array.Empty<SupplierOffer>(), 1) == "No distributor data supplied.", "missing supplier coverage is explicit");
Require(SupplierAvailability.Summary(new[] { new SupplierOffer { Stock = 10 } }, 1).StartsWith("1 listing reports"), "stock with a missing distributor name remains available");
var refreshed = new SupplierOffer { Supplier = "DigiKey", Sku = "PART-ND", Stock = 0, Currency = "EUR", Updated = "Today" };
refreshed.Prices.Add(new PriceBreak { Quantity = 1, UnitPrice = "0,25" });
SupplierAvailability.Merge(offers, new[] { refreshed });
Require(offer.Stock == 0 && offer.Updated == "Today" && new SupplierOfferRow(offer, 1).UnitPrice == "0,25 EUR", "refresh can clear stock and replaces price breaks together with their currency");
var largest = new SupplierOffer { Stock = int.MaxValue };
Require(new SupplierOfferRow(largest, int.MaxValue).CanSupply == true, "large inventories and order quantities do not overflow");
try { _ = new SupplierOfferRow(offer, 0); throw new Exception("Invalid quantity accepted"); }
catch (ArgumentOutOfRangeException) { Console.WriteLine("PASS invalid quantities cannot produce a comparison"); }
