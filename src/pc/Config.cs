using System;
using System.IO;
using System.Text;

namespace Lingwu.Transfer
{
    /// <summary>配置存放位置：优先程序旁边的 data 目录（便携），不可写才退回 AppData，再退回临时目录。</summary>
    static class AppPaths
    {
        static string _dir;

        public static string DataDir
        {
            get
            {
                if (_dir != null) return _dir;

                string[] cands = new string[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "凌雾传输"),
                    Path.Combine(Path.GetTempPath(), "凌雾传输")
                };

                foreach (string c in cands)
                {
                    try
                    {
                        Directory.CreateDirectory(c);
                        string probe = Path.Combine(c, ".__probe");
                        File.WriteAllText(probe, "1");
                        File.Delete(probe);
                        _dir = c;
                        return _dir;
                    }
                    catch { }
                }

                _dir = Path.GetTempPath();
                return _dir;
            }
        }
    }

    /// <summary>配置：配对码、接收目录、是否暂停</summary>
    class Config
    {
        public string Code;
        public string Folder;
        public bool Paused;

        static string FilePath
        {
            get { return Path.Combine(AppPaths.DataDir, "config.txt"); }
        }

        public static string NewCode()
        {
            Random r = new Random(Guid.NewGuid().GetHashCode());
            return r.Next(100000, 1000000).ToString();
        }

        public static Config Load(string defaultFolder)
        {
            Config c = new Config();
            c.Code = NewCode();
            c.Folder = defaultFolder;
            c.Paused = false;

            try
            {
                if (File.Exists(FilePath))
                {
                    string[] lines = File.ReadAllLines(FilePath, Encoding.UTF8);
                    foreach (string line in lines)
                    {
                        int i = line.IndexOf('=');
                        if (i <= 0) continue;
                        string k = line.Substring(0, i).Trim();
                        string v = line.Substring(i + 1).Trim();
                        if (k == "code") c.Code = v;
                        else if (k == "folder" && v.Length > 0) c.Folder = v;
                        else if (k == "paused") c.Paused = (v == "1");
                    }
                }
            }
            catch { }

            if (c.Code == null || c.Code.Length != 6) c.Code = NewCode();
            return c;
        }

        public bool Save()
        {
            try
            {
                string text = "code=" + Code + "\r\n"
                            + "folder=" + Folder + "\r\n"
                            + "paused=" + (Paused ? "1" : "0") + "\r\n";
                File.WriteAllText(FilePath, text, Encoding.UTF8);
                return true;
            }
            catch { return false; }
        }
    }
}
