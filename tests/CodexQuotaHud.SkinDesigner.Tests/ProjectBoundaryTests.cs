using System.Diagnostics;
using System.Text.Json;

namespace CodexQuotaHud.SkinDesigner.Tests;

public sealed class ProjectBoundaryTests
{
    [Fact]
    public void EvaluatedProjectGraph_IsOneWayFromDesignerToAppAndSkins()
    {
        var root = FindRepositoryRoot();

        Assert.Equal(
            ["CodexQuotaHud.App", "CodexQuotaHud.Skins"],
            EvaluatedReferences(root, "src/CodexQuotaHud.SkinDesigner/CodexQuotaHud.SkinDesigner.csproj"));
        Assert.DoesNotContain(
            "CodexQuotaHud.SkinDesigner",
            EvaluatedReferences(root, "src/CodexQuotaHud.App/CodexQuotaHud.App.csproj"));
        Assert.DoesNotContain(
            "CodexQuotaHud.SkinDesigner",
            EvaluatedReferences(root, "src/CodexQuotaHud.Skins/CodexQuotaHud.Skins.csproj"));
        Assert.DoesNotContain(
            "CodexQuotaHud.App",
            EvaluatedReferences(root, "src/CodexQuotaHud.Skins/CodexQuotaHud.Skins.csproj"));
    }

    [Fact]
    public void EvaluatedDesignerOutput_IsWindowsWpfWinExeWithStableName()
    {
        var root = FindRepositoryRoot();
        using var document = Evaluate(
            root,
            "src/CodexQuotaHud.SkinDesigner/CodexQuotaHud.SkinDesigner.csproj",
            "-getProperty:OutputType",
            "-getProperty:TargetFramework",
            "-getProperty:UseWPF",
            "-getProperty:TargetName");
        var properties = document.RootElement.GetProperty("Properties");

        Assert.Equal("WinExe", properties.GetProperty("OutputType").GetString());
        Assert.Equal(
            "net9.0-windows",
            properties.GetProperty("TargetFramework").GetString());
        Assert.Equal("true", properties.GetProperty("UseWPF").GetString());
        Assert.Equal(
            "CodexQuotaHud.SkinDesigner",
            properties.GetProperty("TargetName").GetString());
    }

    private static IReadOnlyList<string> EvaluatedReferences(
        string root,
        string project)
    {
        using var document = Evaluate(root, project, "-getItem:ProjectReference");
        return document.RootElement
            .GetProperty("Items")
            .GetProperty("ProjectReference")
            .EnumerateArray()
            .Select(item => item.GetProperty("Filename").GetString()!)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static JsonDocument Evaluate(
        string root,
        string project,
        params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("msbuild");
        process.StartInfo.ArgumentList.Add(project);
        process.StartInfo.ArgumentList.Add("-nologo");
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        Assert.True(process.Start());
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(30_000));
        Assert.True(
            process.ExitCode == 0,
            $"MSBuild evaluation failed.{Environment.NewLine}{standardOutput}{standardError}");
        var jsonStart = standardOutput.IndexOf('{');
        Assert.True(jsonStart >= 0, $"MSBuild returned no JSON: {standardOutput}");
        return JsonDocument.Parse(standardOutput[jsonStart..]);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CodexQuotaHud.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate CodexQuotaHud.sln.");
    }
}
