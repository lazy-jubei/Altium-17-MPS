using Altium17PartSearch.PartSearch;
using System.Globalization;
int checks = 0;
void Require(bool condition, string description) { if (!condition) throw new Exception(description); checks++; }
void Value(PassiveParameter parameter, string input, double expected)
{
    Require(parameter.TryParse(input, out double actual) && Math.Abs(actual - expected) <= Math.Abs(expected) * 1e-12,
        $"{parameter.Name}: {input} = {expected}");
}
var r = PassiveParameter.Resistance;
var c = PassiveParameter.Capacitance;
Value(r, "10k", 10000); Value(r, "4K7", 4700); Value(r, "0R22", .22); Value(r, "100R", 100);
Value(r, "4.7 kΩ", 4700); Value(r, "2 MOhms", 2000000); Value(r, "2 mOhm", .002);
Value(r, "0 ohms", 0); Value(r, "1e3 Ω", 1000);
Value(c, "100 nF", 1e-7); Value(c, "0.1 µF", 1e-7); Value(c, "0.1 μF", 1e-7);
Value(c, "1000pF", 1e-9); Value(c, "2u2F", 2.2e-6); Value(c, "4N7", 4.7e-9);
Value(c, "47UF", 47e-6); Value(c, "1mF", 1e-3); Value(c, "1e-9 F", 1e-9);
Value(PassiveParameter.Voltage, "6.3V", 6.3); Value(PassiveParameter.Voltage, "2kV", 2000);
Value(PassiveParameter.Power, "100mW", .1); Value(PassiveParameter.Power, "1/8 W", .125);
Value(PassiveParameter.Tolerance, "±5%", 5); Value(PassiveParameter.Tolerance, "+/-0.1%", .1);
foreach (string invalid in new[] { "", "unknown", "-1", "10uF", "1..2", "1/0 W", "Infinity", "NaN", "1e999", "1e-999", new string('9', 400) + "k7", "10 to 20 ohm", "1,000 ohm" })
    Require(!r.TryParse(invalid, out _), "reject invalid/ambiguous resistance: " + invalid);
Require(!c.TryParse("10 kOhm", out _), "reject mismatched capacitance units");
Require(!PassiveParameter.Voltage.TryParse("1mA", out _), "reject mismatched voltage units");
Require(!PassiveParameter.Tolerance.TryParse("5k%", out _), "tolerance has no multiplier");
var range = new ParameterRange(c);
Require(range.Matches(null), "unknown values remain visible with no active bounds");
range.Minimum = "100nF";
Require(range.Error == null && range.Matches(1e-7) && range.Matches(1e-2) && !range.Matches(1e-9) && !range.Matches(null), "minimum only has no implicit maximum; unknown values excluded");
range.Minimum = ""; range.Maximum = "1uF";
Require(range.Matches(0) && range.Matches(1e-6) && !range.Matches(1e-3), "maximum only has no implicit minimum");
range.Minimum = "100nF";
Require(range.Matches(1e-7) && range.Matches(1e-6) && !range.Matches(1e-8) && !range.Matches(1e-5), "inclusive two-sided capacitance bounds");
range.Maximum = "0.1uF";
Require(range.Matches(100e-9), "equal bounds accept equivalent engineering units");
range.Maximum = "1nF";
Require(range.Error != null && !range.Matches(1e-8), "inverted range produces a validation error");
range.Minimum = "invalid";
Require(range.Error != null && !range.Matches(1e-8), "invalid range does not silently become unbounded");
range.Minimum = range.Maximum = " ";
Require(!range.Active && range.Error == null && range.Matches(null), "clearing ranges restores unfiltered results");
var zero = new ParameterRange(r) { Minimum = "0", Maximum = "0" };
Require(zero.Matches(0) && !zero.Matches(null) && !zero.Matches(.000001), "zero-ohm is distinct from unknown and positive resistance");
var parameters = new Dictionary<string,string> { ["Capacitance"] = "100nF", ["Input Capacitance"] = "10pF", ["Voltage Rating"] = "50V", ["Case/Package"] = "0201" };
Require(Math.Abs(c.Read(parameters).Value - 1e-7) < 1e-19 && PassiveParameter.Voltage.Read(parameters) == 50, "read supplier primary/rating fields");
Require(PassiveParameter.Package(parameters) == "0201", "read supplier case/package field");
Require(c.Read(new Dictionary<string,string> { ["Input Capacitance"] = "10pF" }) == null, "do not use incidental parasitic parameters");
Require(r.Read(new Dictionary<string,string> { ["Resistance (Ohms)"] = "1k" }) == 1000, "normalize unit annotations in parameter names");
var values = new[] { "1uF", "100pF", "10nF", "1mF" }.OrderBy(v => c.TryParse(v, out var number) ? number : double.MaxValue).ToArray();
Require(values.SequenceEqual(new[] { "100pF", "10nF", "1uF", "1mF" }), "sort by numeric SI values rather than display text");
Require(PassiveParameter.ForCategory("Resistors").SequenceEqual(new[]{r, PassiveParameter.Power, PassiveParameter.Tolerance}), "resistor category exposes resistance/power/tolerance");
Require(PassiveParameter.ForCategory("Capacitors").SequenceEqual(new[]{c, PassiveParameter.Voltage, PassiveParameter.Tolerance}), "capacitor category exposes capacitance/voltage/tolerance");
Require(PassiveParameter.ForCategory("Transistors").Length == 0, "passive ranges do not leak into other categories");
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
Value(c, "4.7uF", 4.7e-6);
Console.WriteLine($"{checks} passive parameter checks passed.");
