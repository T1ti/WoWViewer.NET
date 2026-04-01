using CASCLib;
using System.Net;

namespace WoWViewer.NET
{
    public static class Listfile
    {
        public static Dictionary<uint, string> FDIDToFilename = new Dictionary<uint, string>();
        public static Dictionary<string, uint> FilenameToFDID = new Dictionary<string, uint>();

        public static void Update()
        {
            try
            {
                using (var client = new WebClient())
                using (var stream = new MemoryStream())
                {
                    var responseStream = client.OpenRead("https://github.com/wowdev/wow-listfile/releases/latest/download/community-listfile.csv");
                    responseStream.CopyTo(stream);
                    File.WriteAllBytes("listfile.csv", stream.ToArray());
                    responseStream.Close();
                    responseStream.Dispose();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Listfile download failed:" + e.Message);
            }
        }

        public static void Load()
        {
            if (!File.Exists("listfile.csv"))
            {
                Update();
            }

            var lines = File.ReadAllLines("listfile.csv");

            foreach (var line in lines)
            {
                string[] tokens = line.Split(';');

                if (tokens.Length != 2)
                {
                    Logger.WriteLine($"Invalid line in listfile: {line}");
                    continue;
                }

                if (!uint.TryParse(tokens[0], out uint fileDataId))
                {
                    Logger.WriteLine($"Invalid line in listfile: {line}");
                    continue;
                }

                FDIDToFilename.Add(fileDataId, tokens[1]);

                if (!FilenameToFDID.ContainsKey(tokens[1]))
                    FilenameToFDID.Add(tokens[1], fileDataId);
            }
        }

        public static bool TryGetFileDataID(string filename, out uint fileDataID)
        {
            var cleaned = filename.ToLower().Replace('\\', '/');

            return FilenameToFDID.TryGetValue(cleaned, out fileDataID);
        }

        public static bool TryGetFilename(uint filedataid, out string filename)
        {
            return FDIDToFilename.TryGetValue(filedataid, out filename);
        }
    }
}
