using System;
using System.Collections.Generic;
using System.IO;

namespace GameplayMusic
{
    public enum Mood { Ambient, Summon, Disaster, Threat }

    // Pure logic, tested without launching Unity.
    public sealed class MusicDirector
    {
        private float disasterUntil, summonUntil, threatUntil;
        private bool lastThreat;
        public void Disaster(float now, float duration) { disasterUntil = now + duration; }
        public void Summon(float now, float duration) { summonUntil = now + duration; }
        public Mood Evaluate(float now, bool threat, float releaseDelay)
        {
            if (lastThreat && !threat) threatUntil = now + releaseDelay;
            lastThreat = threat;
            if (threat || now < threatUntil) return Mood.Threat;
            if (now < disasterUntil) return Mood.Disaster;
            if (now < summonUntil) return Mood.Summon;
            return Mood.Ambient;
        }
        public static bool CanSwitch(Mood current, Mood wanted, float elapsed, float minimum, bool playing)
        { return current != wanted && (!playing || wanted > current || elapsed >= minimum); }
        public void Reset() { disasterUntil = summonUntil = threatUntil = 0f; lastThreat = false; }
    }

    public sealed class MusicLibrary
    {
        private readonly string root;
        private readonly bool shuffle;
        private readonly Random random = new Random();
        private readonly Dictionary<Mood, List<string>> files = new Dictionary<Mood, List<string>>();
        private readonly Dictionary<Mood, Queue<string>> bags = new Dictionary<Mood, Queue<string>>();
        private readonly Dictionary<Mood, string> previous = new Dictionary<Mood, string>();
        private readonly HashSet<string> failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public MusicLibrary(string root, bool shuffle) { this.root = root; this.shuffle = shuffle; }
        public void Refresh()
        {
            foreach (Mood mood in Enum.GetValues(typeof(Mood)))
            {
                var found = new List<string>();
                if (mood == Mood.Ambient) AddFiles(root, found);
                AddFiles(Path.Combine(root, mood.ToString()), found);
                found.Sort(StringComparer.OrdinalIgnoreCase);
                List<string> old;
                if (!files.TryGetValue(mood, out old) || string.Join("\n", old.ToArray()) != string.Join("\n", found.ToArray())) bags.Remove(mood);
                files[mood] = found;
            }
        }
        private void AddFiles(string folder, List<string> found)
        {
            if (!Directory.Exists(folder)) return;
            foreach (string path in Directory.GetFiles(folder))
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if ((ext == ".ogg" || ext == ".mp3" || ext == ".wav") && !failed.Contains(path)) found.Add(path);
            }
        }
        public bool Has(Mood mood) { List<string> list; return files.TryGetValue(mood, out list) && list.Count > 0; }
        public Mood Resolve(Mood wanted) { return Has(wanted) ? wanted : Mood.Ambient; }
        public string Next(Mood mood)
        {
            if (!Has(mood)) return null;
            Queue<string> bag;
            if (!bags.TryGetValue(mood, out bag) || bag.Count == 0)
            {
                var list = new List<string>(files[mood]);
                if (shuffle)
                {
                    for (int i = list.Count - 1; i > 0; i--)
                    { int j = random.Next(i + 1); string swap = list[i]; list[i] = list[j]; list[j] = swap; }
                    string last;
                    if (list.Count > 1 && previous.TryGetValue(mood, out last) && list[0] == last)
                    { string swap = list[0]; list[0] = list[1]; list[1] = swap; }
                }
                bags[mood] = bag = new Queue<string>(list);
            }
            string next = bag.Dequeue(); previous[mood] = next; return next;
        }
        public void Reject(string path)
        { failed.Add(path); foreach (var list in files.Values) list.Remove(path); bags.Clear(); }
        public void Reset() { files.Clear(); bags.Clear(); previous.Clear(); failed.Clear(); }
    }
}
