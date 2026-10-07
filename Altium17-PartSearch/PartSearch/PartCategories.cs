using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Altium17PartSearch.PartSearch
{
    internal sealed class PartCategory
    {
        public string Name { get; }
        internal string Keyword { get; }
        private readonly Regex terms;
        private PartCategory(string name, string keyword, string pattern)
        {
            Name = name; Keyword = keyword;
            terms = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        internal static readonly PartCategory[] All = {
            new PartCategory("All parts", "", ""),
            new PartCategory("Resistors", "resistor", @"\b(resistors?|resistance|potentiometers?|RES)\b"),
            new PartCategory("Capacitors", "capacitor", @"\b(capacitors?|capacitance|CAP)\b"),
            new PartCategory("Inductors", "inductor", @"\b(inductors?|inductance|chokes?|coils?)\b"),
            new PartCategory("LEDs", "LED", @"\b(LEDs?|light emitting)\b"),
            new PartCategory("Diodes", "diode", @"\b(diodes?|rectifiers?|zener|schottky)\b"),
            new PartCategory("Transistors", "transistor", @"\b(transistors?|MOSFETs?|FETs?|BJTs?|IGBTs?)\b"),
            new PartCategory("FPGAs", "FPGA", @"\b(FPGAs?|CPLDs?|programmable logic)\b"),
            new PartCategory("Integrated circuits", "IC", @"\b(IC|integrated circuits?|microcontrollers?|processors?|amplifiers?|OPAMP|regulators?|memory)\b"),
            new PartCategory("Connectors", "connector", @"\b(connectors?|headers?|receptacles?|CONN)\b"),
            new PartCategory("Crystals & oscillators", "crystal", @"\b(crystals?|oscillators?|resonators?)\b")
        };
        internal string SearchQuery(string query) => string.IsNullOrWhiteSpace(query) ? Keyword : query.Trim();
        internal bool Matches(string category, string description, IEnumerable<string> parameters)
        {
            if (Keyword.Length == 0) return true;
            return Classify(category, description, parameters) == this;
        }
        private static PartCategory Classify(string category, string description, IEnumerable<string> parameters)
        {
            // Provider categories and descriptions precede electrical parameters:
            // a transistor's capacitance must not turn it into a capacitor.
            return All.Skip(1).FirstOrDefault(c => c.terms.IsMatch(category ?? ""))
                ?? All.Skip(1).FirstOrDefault(c => c.terms.IsMatch(description ?? ""))
                ?? All.Skip(1).FirstOrDefault(c => c.terms.IsMatch(string.Join(" ", parameters.Where(p =>
                    p.Equals("Resistance", StringComparison.OrdinalIgnoreCase) || p.Equals("Capacitance", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals("Inductance", StringComparison.OrdinalIgnoreCase)))));
        }
        internal static string Display(string category, string description, IEnumerable<string> parameters)
        {
            var match = Classify(category, description, parameters);
            return match?.Name ?? category ?? "";
        }
    }
}
