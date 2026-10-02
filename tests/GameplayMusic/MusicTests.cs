using System;
using System.IO;
using System.Collections.Generic;
using GameplayMusic;

class MusicTests
{
    static int count;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
    static void Main(string[] args)
    {
        var d = new MusicDirector();
        Check(d.Evaluate(0, false, 8) == Mood.Ambient, "idle ambience");
        d.Summon(1, 12);
        Check(d.Evaluate(2, false, 8) == Mood.Summon, "summoning cue");
        d.Disaster(3, 25);
        Check(d.Evaluate(4, false, 8) == Mood.Disaster, "disaster overrides summon");
        Check(d.Evaluate(5, true, 8) == Mood.Threat, "retaliation overrides disaster");
        Check(d.Evaluate(6, false, 8) == Mood.Threat, "retaliation release delay");
        Check(d.Evaluate(13.9f, false, 8) == Mood.Threat, "release timer not reset each frame");
        Check(d.Evaluate(14, false, 8) == Mood.Disaster, "returns to still-active disaster");
        Check(d.Evaluate(29, false, 8) == Mood.Ambient, "expired cues return to ambient");
        d.Disaster(30, 25); d.Disaster(50, 25);
        Check(d.Evaluate(60, false, 8) == Mood.Disaster, "continuing spread extends disaster");
        d.Reset();
        Check(d.Evaluate(0, false, 8) == Mood.Ambient, "world exit clears events");
        Check(d.Evaluate(0, true, 8) == Mood.Threat, "loaded retaliation is recognized immediately");
        Check(!MusicDirector.CanSwitch(Mood.Disaster, Mood.Ambient, 2, 12, true), "minimum hold prevents rapid return");
        Check(MusicDirector.CanSwitch(Mood.Summon, Mood.Threat, 2, 12, true), "urgent event bypasses hold");
        Check(MusicDirector.CanSwitch(Mood.Disaster, Mood.Ambient, 12, 12, true), "hold eventually releases");
        Check(!MusicDirector.CanSwitch(Mood.Disaster, Mood.Disaster, 50, 12, true), "repeated events do not restart mood");
        string root = Path.Combine(args[0], "playlist-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "Threat"));
        Directory.CreateDirectory(Path.Combine(root, "Ambient"));
        var lib = new MusicLibrary(root, false); lib.Refresh();
        Check(lib.Next(Mood.Ambient) == null, "empty playlist safe");
        string a = Path.Combine(root, "01 old song.MP3"), b = Path.Combine(root, "02 old song.ogg");
        File.WriteAllText(a, "test"); File.WriteAllText(b, "test"); File.WriteAllText(Path.Combine(root, "ignore.txt"), "test");
        lib.Refresh();
        Check(lib.Resolve(Mood.Threat) == Mood.Ambient, "empty event folder falls back");
        Check(lib.Next(Mood.Ambient) == a && lib.Next(Mood.Ambient) == b && lib.Next(Mood.Ambient) == a, "legacy root, filename order, looping and extensions");
        string t = Path.Combine(root, "Threat", "battle.wav"); File.WriteAllText(t, "test"); lib.Refresh();
        Check(lib.Resolve(Mood.Threat) == Mood.Threat && lib.Next(Mood.Threat) == t, "new event track discovered");
        lib.Reject(t); lib.Refresh();
        Check(lib.Resolve(Mood.Threat) == Mood.Ambient, "failed event track falls back after refresh");
        lib.Reset(); lib.Refresh();
        Check(lib.Has(Mood.Threat), "world reset allows retry of repaired files");
        lib.Reject(a); lib.Reject(b);
        Check(lib.Next(Mood.Ambient) == null, "all corrupt ambient tracks exhaust safely");
        string c = Path.Combine(root, "Ambient", "new.wav"); File.WriteAllText(c, "test"); lib.Refresh();
        Check(lib.Next(Mood.Ambient) == c, "Ambient subfolder supported");
        var shuffled = new MusicLibrary(root, true); shuffled.Refresh();
        string last = null;
        for (int pass = 0; pass < 20; pass++)
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < 3; i++)
            {
                string next = shuffled.Next(Mood.Ambient);
                if (i == 0 && next == last) throw new Exception("shuffle repeats at boundary");
                if (!seen.Add(next)) throw new Exception("shuffle repeats within pass");
                last = next;
            }
        }
        Check(true, "20 shuffled passes: every track once, no boundary repeat");
        Console.WriteLine("All " + count + " checks passed.");
    }
}
