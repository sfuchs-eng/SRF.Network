using System.Text;

namespace SRF.Network.Knx.Domain;

public class DefaultLabelToNameConverter : ILabelToNameConverter
{
    public string GetName(string label)
    {
        // legacy code copied over... todo: do better.
        StringBuilder l = new(label);

        Dictionary<string,string> replacers = new() {
            {" - ", "_"},
            {" ", "_"},
            {"ä", "ae"},
            {"ö", "oe"},
            {"ü", "ue"}
        };
        foreach (var r in replacers)
            l = l.Replace (r.Key, r.Value);

        var killS = new string[] { ",", ";", "-", ":", "(", ")", "/", ".", "+" };
        foreach (string s in killS)
            l = l.Replace (s, "");

        while (l.ToString().Contains("  "))
            l.Replace("  ", " ");

        return l.ToString ();
    }
}
