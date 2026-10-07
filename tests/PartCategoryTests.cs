using Altium17PartSearch.PartSearch;
PartCategory Category(string name) => PartCategory.All.Single(c => c.Name == name);
void Require(bool condition, string description) { if (!condition) throw new Exception(description); Console.WriteLine("PASS " + description); }
Require(Category("Resistors").SearchQuery("") == "resistor", "category browsing supplies a provider keyword");
Require(Category("Capacitors").SearchQuery("  GRM188R71H104KA93D  ") == "GRM188R71H104KA93D", "an exact MPN survives category filtering unchanged");
Require(Category("All parts").Matches(null, null, Array.Empty<string>()), "all parts retains uncategorized results");
Require(Category("Resistors").Matches("Passive / Resistors", "", Array.Empty<string>()), "native resistor categories are recognized");
Require(Category("Capacitors").Matches("", "CAP CER 100NF 50V", Array.Empty<string>()), "abbreviated capacitor descriptions are recognized");
Require(Category("Transistors").Matches(null, "Bipolar transistor NPN", new[]{"Capacitance", "Resistance"}), "transistor descriptions take precedence over incidental electrical parameters");
Require(!Category("Resistors").Matches("Transistors", "Resistance sensor", new[]{"Resistance"}), "provider category takes precedence over incidental description words");
Require(Category("Resistors").Matches("", "", new[]{"Resistance"}), "exact primary parameters classify otherwise unidentified parts");
Require(!Category("Capacitors").Matches("", "", new[]{"Input Capacitance"}), "parasitic parameter names alone do not classify a part");
Require(Category("FPGAs").Matches("Integrated circuits / FPGAs", "", Array.Empty<string>()), "specific FPGA categories take precedence over generic IC categories");
Require(Category("LEDs").Matches("Diodes / LEDs", "", Array.Empty<string>()), "LED subcategories take precedence over generic diodes");
Require(!Category("Integrated circuits").Matches("FPGAs", "IC FPGA", Array.Empty<string>()), "specific categories do not leak into general IC results");
Require(PartCategory.Display(null, null, Array.Empty<string>()) == "", "unknown categories are not invented");
Require(PartCategory.Display("Custom category", "", Array.Empty<string>()) == "Custom category", "unrecognized provider labels remain visible");
Require(!Category("Transistors").Matches("", "FETAL medical connector", Array.Empty<string>()), "category keywords match whole words");
Require(PartCategory.All.Select(c=>c.Name).Distinct().Count()==PartCategory.All.Length, "category labels are unique");
