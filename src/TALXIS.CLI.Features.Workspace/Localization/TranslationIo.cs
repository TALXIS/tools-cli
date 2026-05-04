using System.Text.Json;
using TALXIS.CLI.Core.Storage;

namespace TALXIS.CLI.Features.Workspace.Localization;

public static class TranslationIo
{
    public static void Write(string path, TranslationFile file)
    {
        var dir = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        using var stream = System.IO.File.Create(path);

        JsonSerializer.Serialize(stream, file, TxcJsonOptions.Default);
    }

    public static TranslationFile Read(string path)
    {
        using var stream = System.IO.File.OpenRead(path);
        
        return JsonSerializer.Deserialize<TranslationFile>(stream, TxcJsonOptions.Default)
            ?? throw new InvalidOperationException($"Could not parse translation file: {path}");
    }
}
