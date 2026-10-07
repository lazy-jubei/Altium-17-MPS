using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Altium17PartSearch.PartSearch
{
    internal sealed class PassiveParameter
    {
        public string Name { get; }
        public string Example { get; }
        private readonly string unit;
        private readonly HashSet<string> aliases;
        private PassiveParameter(string name, string unit, string example, params string[] aliases)
        { Name = name; this.unit = unit; Example = example; this.aliases = new HashSet<string>(aliases.Select(Key)); }

        internal static readonly PassiveParameter Resistance = new PassiveParameter("Resistance", "ohm", "10k, 4.7 kOhm", "resistance", "resistanceohms", "resistanceohm");
        internal static readonly PassiveParameter Capacitance = new PassiveParameter("Capacitance", "F", "100 nF, 1 uF", "capacitance", "capacitancefarads");
        internal static readonly PassiveParameter Voltage = new PassiveParameter("Voltage", "V", "6.3 V, 50 V", "voltagerating", "voltage rated", "ratedvoltage", "voltageratingdc", "voltage");
        internal static readonly PassiveParameter Power = new PassiveParameter("Power", "W", "100 mW, 1/4 W", "powerrating", "powerwatts", "power rating watts", "power");
        internal static readonly PassiveParameter Tolerance = new PassiveParameter("Tolerance", "%", "1%, 5%", "tolerance", "resistancetolerance", "capacitancetolerance");
        internal static PassiveParameter Primary(string category) => category == "Resistors" ? Resistance : category == "Capacitors" ? Capacitance : null;
        internal static PassiveParameter[] ForCategory(string category)
        {
            var primary = Primary(category);
            return primary == null ? Array.Empty<PassiveParameter>() : new[] { primary, primary == Resistance ? Power : Voltage, Tolerance };
        }
        private static string Key(string text) => Regex.Replace(text ?? "", @"[^a-z0-9]", "", RegexOptions.IgnoreCase).ToLowerInvariant();
        internal string ReadText(IEnumerable<KeyValuePair<string, string>> parameters) => parameters
            .Where(p => aliases.Contains(Key(p.Key))).Select(p => p.Value).FirstOrDefault() ?? "";
        internal double? Read(IEnumerable<KeyValuePair<string, string>> parameters) => TryParse(ReadText(parameters), out var value) ? value : (double?)null;
        internal static string Package(IEnumerable<KeyValuePair<string, string>> parameters) => parameters
            .Where(p => new[] { "casepackage", "packagecase", "package", "casesize", "packagesize" }.Contains(Key(p.Key)))
            .Select(p => p.Value).FirstOrDefault() ?? "";

        internal bool TryParse(string text, out double value)
        {
            value = 0;
            text = Regex.Replace((text ?? "").Trim().Replace('µ', 'u').Replace('μ', 'u').Replace('Ω', 'Ω'), @"\s+", "");
            if (this == Tolerance) text = Regex.Replace(text, @"^(±|\+/-)", "");
            string suffix = unit == "ohm" ? @"(?:ohms?|Ω|R)" : Regex.Escape(unit);
            var match = Regex.Match(text, @"^(?<number>(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?|\d+/\d+)(?<prefix>[pPnNuUmkKMG]?)(?<unit>" + suffix + @")?$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string number = match.Groups["number"].Value;
                var fraction = number.Split('/');
                if (!double.TryParse(fraction[0], NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
                if (value == 0 && fraction[0].Split('e', 'E')[0].Any(ch => ch >= '1' && ch <= '9')) return false;
                if (fraction.Length == 2)
                {
                    if (!double.TryParse(fraction[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double denominator) || denominator == 0) return false;
                    value /= denominator;
                }
                string prefix = match.Groups["prefix"].Value;
                if (unit == "%" && prefix.Length != 0) return false;
                double unscaled = value;
                value *= Scale(prefix);
                if (unscaled > 0 && value == 0) return false;
            }
            else
            {
                // Engineering notation uses the multiplier as a decimal point: 4k7, 0R22, 2u2F.
                match = Regex.Match(text, @"^(?<whole>\d+)(?<prefix>[RrpkKnNuUmMG])(?<fraction>\d+)(?:" + suffix + @")?$", RegexOptions.IgnoreCase);
                if (!match.Success || unit == "%" || (match.Groups["prefix"].Value.ToUpperInvariant() == "R" && unit != "ohm")) return false;
                if (!double.TryParse(match.Groups["whole"].Value + "." + match.Groups["fraction"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
                value *= Scale(match.Groups["prefix"].Value);
            }
            return value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }
        private static double Scale(string prefix) => prefix switch
        {
            "p" or "P" => 1e-12, "n" or "N" => 1e-9, "u" or "U" => 1e-6,
            "m" => 1e-3, "k" or "K" => 1e3, "M" => 1e6, "G" or "g" => 1e9, _ => 1
        };
    }

    internal sealed class ParameterRange : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public PassiveParameter Parameter { get; }
        public string Name => Parameter.Name;
        public string Example => Parameter.Example;
        private string minimum = "", maximum = "";
        public string Minimum { get => minimum; set { minimum = value ?? ""; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Minimum))); } }
        public string Maximum { get => maximum; set { maximum = value ?? ""; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Maximum))); } }
        public bool Active => !string.IsNullOrWhiteSpace(Minimum) || !string.IsNullOrWhiteSpace(Maximum);
        internal ParameterRange(PassiveParameter parameter) { Parameter = parameter; }
        internal string Error
        {
            get
            {
                if (!Bounds(out var min, out var max)) return Name + ": use " + Example + ".";
                return min.HasValue && max.HasValue && Compare(min.Value, max.Value) > 0 ? Name + ": minimum exceeds maximum." : null;
            }
        }
        private bool Bounds(out double? min, out double? max)
        {
            min = max = null;
            if (!string.IsNullOrWhiteSpace(Minimum)) { if (!Parameter.TryParse(Minimum, out double value)) return false; min = value; }
            if (!string.IsNullOrWhiteSpace(Maximum)) { if (!Parameter.TryParse(Maximum, out double value)) return false; max = value; }
            return true;
        }
        internal bool Matches(double? value)
        {
            if (!Active) return true;
            if (!Bounds(out var min, out var max) || Error != null || !value.HasValue) return false;
            // Equivalent engineering units can differ by a last binary digit.
            return (!min.HasValue || Compare(value.Value, min.Value) >= 0) && (!max.HasValue || Compare(value.Value, max.Value) <= 0);
        }
        private static int Compare(double a, double b) => Math.Abs(a - b) <= Math.Max(Math.Abs(a), Math.Abs(b)) * 1e-12 ? 0 : a.CompareTo(b);
    }
}
