using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NomadDriveWardrobe
{
    /// <summary>Turns the game's internal cosmetic ids into readable names.</summary>
    internal static class Names
    {
        // "CosmeticSet2" -> "Outfit 2"
        internal static string Outfit(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Outfit";
            var m = Regex.Match(name, @"^CosmeticSet\s*(\d+)$", RegexOptions.IgnoreCase);
            return m.Success ? "Outfit " + m.Groups[1].Value : Words(name);
        }

        /// <summary>
        /// Readable names for a group of ids of one category, for example
        /// Boots-Male-01 -> "Boots", Pants-02-Male -> "Pants 2", Hair-01-ForHats -> "Hair (for hats)".
        /// A number is only shown when the group has more than one variant of the same base name.
        /// </summary>
        internal static Dictionary<string, string> ForGroup(IEnumerable<string> ids, bool showGender)
        {
            var parsed = ids.Distinct().ToDictionary(i => i, Parse);
            var baseCount = parsed.Values.GroupBy(p => p.Base).ToDictionary(g => g.Key, g => g.Count());

            var result = new Dictionary<string, string>();
            foreach (var kv in parsed)
            {
                var p = kv.Value;
                var sb = new StringBuilder(p.Base);
                if (p.Number > 0 && baseCount[p.Base] > 1) sb.Append(' ').Append(p.Number);
                if (p.ForHats) sb.Append(" (for hats)");
                if (showGender && p.Gender != null) sb.Append(" (").Append(p.Gender).Append(')');
                result[kv.Key] = sb.ToString();
            }

            // Two different ids can still collapse to the same text; keep them distinguishable.
            foreach (var dup in result.GroupBy(r => r.Value).Where(g => g.Count() > 1))
                foreach (var r in dup) result[r.Key] = r.Value + " [" + r.Key + "]";
            return result;
        }

        internal static string Single(string id)
        {
            return string.IsNullOrEmpty(id) ? "None" : ForGroup(new[] { id }, false)[id];
        }

        private class Parsed
        {
            public string Base = "";
            public int Number;
            public bool ForHats;
            public string Gender;
        }

        private static Parsed Parse(string id)
        {
            var p = new Parsed();
            var baseTokens = new List<string>();
            foreach (var raw in id.Split('-', '_'))
            {
                if (raw.Length == 0) continue;
                if (raw.Equals("Male", StringComparison.OrdinalIgnoreCase)) { p.Gender = "Male"; continue; }
                if (raw.Equals("Female", StringComparison.OrdinalIgnoreCase)) { p.Gender = "Female"; continue; }
                if (raw.Equals("ForHats", StringComparison.OrdinalIgnoreCase)) { p.ForHats = true; continue; }
                if (Regex.IsMatch(raw, @"^\d+$")) { int.TryParse(raw, out p.Number); continue; }
                baseTokens.Add(raw);
            }

            var joined = string.Join(" ", baseTokens.ToArray());
            // "T shirt" is written "T-shirt" in the ids; show it the usual way.
            joined = Regex.Replace(joined, @"^T\s*shirt$", "T-Shirt", RegexOptions.IgnoreCase);
            p.Base = Words(joined);
            return p;
        }

        // "CowboyHat" -> "Cowboy Hat", "FlipFlops" -> "Flip Flops"
        private static string Words(string s)
        {
            s = Regex.Replace(s, @"(?<=[a-z])(?=[A-Z])", " ");
            s = s.Replace('_', ' ').Trim();
            return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }
}
