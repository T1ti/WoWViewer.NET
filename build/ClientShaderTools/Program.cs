using WoWViewer.ClientShaderTools;

try
{
    if (args.Length == 0 || args.Contains("--help"))
    {
        Console.WriteLine("ClientShaderTools --shader-root <shaders directory> --output <cache directory> " +
            "[--profile SM3|AllDx9] [--force] [--verify-hashes]");
        return args.Length == 0 ? 2 : 0;
    }
    string? root = null, output = null;
    var profile = "SM3";
    var force = false;
    var verify = false;
    for (var i = 0; i < args.Length; i++)
    {
        string Value() => ++i < args.Length ? args[i]
            : throw new ArgumentException($"Missing value for {args[i - 1]}.");
        switch (args[i])
        {
            case "--shader-root": root = Value(); break;
            case "--output": output = Value(); break;
            case "--profile": profile = Value(); break;
            case "--force": force = true; break;
            case "--verify-hashes": verify = true; break;
            default: throw new ArgumentException($"Unknown argument: {args[i]}");
        }
    }
    if (root is null || output is null)
        throw new ArgumentException("Both --shader-root and --output are required.");
    var report = new ShaderCache(new Dx9Disassembler()).Run(root, output, profile, force, verify);
    Console.WriteLine($"Files: {report.Files}; processed: {report.Processed}; reused: {report.Reused}; " +
        $"permutations: {report.Permutations}; unique programs: {report.UniquePrograms}; " +
        $"disassembled: {report.Disassembled}; empty files: {report.EmptyFiles}; errors: {report.Errors.Count}.");
    Console.WriteLine($"Cached index: {Path.Combine(Path.GetFullPath(output), "index.md")}");
    foreach (var error in report.Errors)
        Console.Error.WriteLine(error);
    return report.Errors.Count == 0 ? 0 : 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
