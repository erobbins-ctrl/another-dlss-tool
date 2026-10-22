using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;

namespace Dlss5Setup
{
    // ReShade add-ons were removed from the app. This only cleans up add-ons that an
    // earlier version installed, when the feeder is uninstalled from a game.
    [DataContract]
    class AddonRecord
    {
        [DataMember] public Dictionary<string, string[]> Installed = new Dictionary<string, string[]>();
    }

    static class Addons
    {
        public static void RemoveAll(GameEntry g)
        {
            var r = Json.Read<AddonRecord>(Path.Combine(Paths.State(g), "addons.json"));
            if (r == null || r.Installed == null) return;
            foreach (var files in r.Installed.Values)
                foreach (var f in files)
                {
                    string p = Path.Combine(g.Folder, f);
                    try { if (File.Exists(p)) File.Delete(p); } catch { }
                }
        }
    }
}
