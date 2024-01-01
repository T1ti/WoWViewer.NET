using SceneScriptLib;
using Newtonsoft.Json;

namespace SceneScriptTest
{
    internal class Program
    {
        static void Main(string[] args)
        {
            foreach (var file in Directory.GetFiles("G:\\NewmansLandingProject\\scenes\\10.0.0 Newman's Landing Machinima\\", "*.lua"))
            {
                Console.WriteLine(Path.GetFileNameWithoutExtension(file));
                var pathFileName = Path.GetFileNameWithoutExtension(file);
                if (pathFileName.Contains("Documentation"))
                    continue;
                
                var contents = File.ReadAllText(file);
                var script = SceneScriptReader.ParseTimelineScript(contents);


               Console.WriteLine(JsonConvert.SerializeObject(script, Formatting.Indented, new JsonSerializerSettings() { NullValueHandling = NullValueHandling.Ignore}));
            }
        }
    }
}
