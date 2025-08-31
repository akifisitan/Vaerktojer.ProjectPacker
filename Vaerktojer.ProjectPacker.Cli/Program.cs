using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vaerktojer.ProjectPacker.Cli;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
[JsonSerializable(typeof(List<FileData>))]
public partial class AppJsonSerializerContext : JsonSerializerContext { }

public record FileData(string Path, string Content);

internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        try
        {
            if (
                args.ElementAtOrDefault(0) == "pack"
                && args.ElementAtOrDefault(1) is not null
                && args.ElementAtOrDefault(2) is not null
            )
            {
                if (!Directory.Exists(args[1]))
                {
                    throw new Exception($"Directory {args[1]} does not exist.");
                }

                await Pack(args[1], args[2]);
                return;
            }

            if (
                args.ElementAtOrDefault(0) == "unpack"
                && args.ElementAtOrDefault(1) is not null
                && args.ElementAtOrDefault(2) is not null
            )
            {
                if (!File.Exists(args[1]))
                {
                    throw new Exception("Import file does not exist.");
                }

                if (Directory.Exists(args[2]))
                {
                    throw new Exception($"Directory {args[2]} already exists.");
                }

                Directory.CreateDirectory(args[2]);

                await Unpack(args[1], args[2]);
                return;
            }

            ShowHelpAndExit();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private static void ShowHelpAndExit()
    {
        var executableName = Path.GetFileNameWithoutExtension(Environment.ProcessPath);
        Console.WriteLine(
            $"""
            Usage:
                {executableName} pack <path/to/project/directory> <outputMetadataFileName>
                {executableName} unpack <path/to/metadata/file> <path/to/output/directory>
            """
        );
        Environment.Exit(1);
    }

    private static async Task Pack(
        string basePath,
        string outputFilePath,
        CancellationToken cancellationToken = default
    )
    {
        List<FileData> importData = [];

        foreach (
            var filePath in EnumerateFiles(
                basePath,
                IncludeFiles,
                IgnoreDirectories,
                cancellationToken: cancellationToken
            )
        )
        {
            importData.Add(
                new FileData(
                    Path.GetRelativePath(basePath, filePath),
                    await File.ReadAllTextAsync(filePath, cancellationToken)
                )
            );
        }

        await File.WriteAllTextAsync(
            outputFilePath,
            JsonSerializer.Serialize(importData, AppJsonSerializerContext.Default.ListFileData),
            cancellationToken
        );
    }

    private static async Task Unpack(
        string importFilePath,
        string outputDirectoryPath,
        CancellationToken cancellationToken = default
    )
    {
        var importFileContent = await File.ReadAllTextAsync(importFilePath, cancellationToken);
        var importData =
            JsonSerializer.Deserialize(
                importFileContent,
                AppJsonSerializerContext.Default.ListFileData
            ) ?? throw new Exception("Import data not found or not valid json.");

        foreach (var fileData in importData)
        {
            var filePath = Path.Combine(outputDirectoryPath, fileData.Path);

            var dirPath = Path.GetDirectoryName(filePath)!;

            if (!Directory.Exists(dirPath))
            {
                Directory.CreateDirectory(dirPath);
            }

            await File.WriteAllTextAsync(filePath, fileData.Content, cancellationToken);
        }
    }

    private static bool IncludeFiles(string path)
    {
        var ext = Path.GetExtension(path);

        return ext == ".cs"
            || ext == ".csproj"
            || ext == ".sln"
            || ext == ".editorconfig"
            || ext == ".xaml"
            || ext == ".vsct"
            || ext == ".vsixmanifest";
    }

    private static bool IgnoreDirectories(string path)
    {
        return path.Contains("bin") || path.Contains("obj");
    }

    private static IEnumerable<string> EnumerateFiles(
        string rootPath,
        Func<string, bool>? includeFilePredicate = null,
        Func<string, bool>? ignoreDirPredicate = null,
        EnumerationOptions? enumerationOptions = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {rootPath}");
        }

        includeFilePredicate ??= _ => true;
        ignoreDirPredicate ??= _ => false;

        enumerationOptions ??= new EnumerationOptions { IgnoreInaccessible = true };

        var directoryStack = new Stack<string>();
        directoryStack.Push(rootPath);

        while (directoryStack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentPath = directoryStack.Pop();

            foreach (var filePath in Directory.EnumerateFiles(currentPath, "*", enumerationOptions))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (includeFilePredicate(filePath))
                {
                    yield return filePath;
                }
            }

            foreach (
                var dirPath in Directory.EnumerateDirectories(currentPath, "*", enumerationOptions)
            )
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ignoreDirPredicate(dirPath))
                {
                    directoryStack.Push(dirPath);
                }
            }
        }
    }
}
