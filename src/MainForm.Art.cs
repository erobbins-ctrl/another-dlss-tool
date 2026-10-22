using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace Dlss5Setup
{
    partial class MainForm
    {
        readonly Dictionary<string, Image> artCache = new Dictionary<string, Image>();
        readonly HashSet<string> artLoading = new HashSet<string>();

        // Returns cached art, or null while it loads in the background (the list and header refresh when it arrives).
        Image ArtFor(GameEntry g, string kind)
        {
            if (g == null || string.IsNullOrEmpty(g.Exe)) return null;
            string key = g.Id + "|" + kind;
            Image img;
            if (artCache.TryGetValue(key, out img)) return img;
            if (!artLoading.Add(key)) return null;
            Task.Run(() =>
            {
                bool changed = false;
                if (string.IsNullOrEmpty(g.SteamAppId)) { g.SteamAppId = Art.SteamAppIdFromPath(g.Exe); changed = g.SteamAppId != null; }
                if (string.IsNullOrEmpty(g.SteamAppId) && !g.ArtLookedUp)
                {
                    g.SteamAppId = Art.SteamAppIdFromStore(g.Name);
                    g.ArtLookedUp = true; changed = true;
                }
                return Tuple.Create(Art.Load(g.SteamAppId, g.Exe, kind), changed);
            }).ContinueWith(t =>
            {
                artLoading.Remove(key);
                artCache[key] = t.IsFaulted ? null : t.Result.Item1;
                if (!t.IsFaulted && t.Result.Item2) SaveLibrary();
                list.Invalidate();
                if (Selected == g && kind == Art.Header) hero.Art.SetImage(artCache[key]);
            }, TaskScheduler.FromCurrentSynchronizationContext());
            return null;
        }

        // Art follows the exe; forget it when the exe changes.
        void ForgetArt(GameEntry g)
        {
            foreach (var k in artCache.Keys.Where(k => k.StartsWith(g.Id + "|")).ToList()) artCache.Remove(k);
            g.SteamAppId = null; g.ArtLookedUp = false;
        }
    }
}
