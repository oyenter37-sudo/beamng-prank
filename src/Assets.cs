using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace BeamPrank
{
    /// <summary>Loads embedded UI assets and the loading-screen frame sequence.</summary>
    static class Assets
    {
        const string Prefix = "beamprank.assets.";
        const int LruMax = 14;

        static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, Bitmap> Cache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<int, Bitmap> Frames = new Dictionary<int, Bitmap>();
        static readonly LinkedList<int> Lru = new LinkedList<int>();
        static Icon _icon;

        public const int FrameCount = 79;

        static Assets()
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            foreach (string n in asm.GetManifestResourceNames())
                Names[n] = n;
        }

        static Stream Open(string path)
        {
            string key = Prefix + path.Replace('/', '.').Replace('\\', '.');
            string real;
            if (!Names.TryGetValue(key, out real))
            {
                string suffix = key.Substring(Prefix.Length);
                foreach (KeyValuePair<string, string> kv in Names)
                {
                    if (kv.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        real = kv.Value;
                        break;
                    }
                }
            }
            if (real == null)
                throw new FileNotFoundException("Missing embedded asset: " + path);
            return Assembly.GetExecutingAssembly().GetManifestResourceStream(real);
        }

        public static Bitmap Get(string path)
        {
            Bitmap b;
            if (Cache.TryGetValue(path, out b))
                return b;
            using (Stream s = Open(path))
            using (Bitmap tmp = new Bitmap(s))
                b = new Bitmap(tmp);
            Cache[path] = b;
            return b;
        }

        public static Bitmap Frame(int index)
        {
            if (index < 0) index = 0;
            if (index >= FrameCount) index = FrameCount - 1;

            Bitmap b;
            if (Frames.TryGetValue(index, out b))
            {
                Touch(index);
                return b;
            }

            using (Stream s = Open(string.Format("load/f{0:000}.jpg", index)))
            using (Bitmap tmp = new Bitmap(s))
                b = new Bitmap(tmp);

            Frames[index] = b;
            Touch(index);

            while (Lru.Count > LruMax)
            {
                int old = Lru.First.Value;
                Lru.RemoveFirst();
                Bitmap dead;
                if (old != index && Frames.TryGetValue(old, out dead))
                {
                    Frames.Remove(old);
                    dead.Dispose();
                }
            }
            return b;
        }

        static void Touch(int i)
        {
            Lru.Remove(i);
            Lru.AddLast(i);
        }

        public static Icon AppIcon
        {
            get
            {
                if (_icon == null)
                {
                    using (Stream s = Open("app.ico"))
                        _icon = new Icon(s, 32, 32);
                }
                return _icon;
            }
        }

        public static Bitmap AppIconImage
        {
            get { return Get("app32.png"); }
        }
    }
}
