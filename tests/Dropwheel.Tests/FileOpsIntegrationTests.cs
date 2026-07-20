using System.Diagnostics;
using System.IO;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.Tests;

[Collection("WindowsIntegration")]
public sealed class FileOpsIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "dw_shell_" + Guid.NewGuid().ToString("N"));

    public FileOpsIntegrationTests() => Directory.CreateDirectory(_root);

    public void Dispose() => TempDir.Delete(_root);

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void Shell_copy_copies_a_real_file_in_silent_mode()
    {
        var sourceDir = Directory.CreateDirectory(Path.Combine(_root, "source folder"));
        var destinationDir = Directory.CreateDirectory(Path.Combine(_root, "destination folder"));
        var source = Path.Combine(sourceDir.FullName, "данные с пробелом.bin");
        var payload = new byte[] { 0, 1, 2, 127, 128, 254, 255 };
        File.WriteAllBytes(source, payload);

        Assert.True(FileOps.Execute(new[] { source }, destinationDir.FullName, DropAction.Copy, silent: true));
        Assert.Equal(payload, File.ReadAllBytes(Path.Combine(destinationDir.FullName, Path.GetFileName(source))));
        Assert.Equal(payload, File.ReadAllBytes(source));
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void Shell_move_moves_a_real_file_in_silent_mode()
    {
        var sourceDir = Directory.CreateDirectory(Path.Combine(_root, "source folder"));
        var destinationDir = Directory.CreateDirectory(Path.Combine(_root, "destination folder"));
        var source = Path.Combine(sourceDir.FullName, "переместить файл.txt");
        File.WriteAllText(source, "payload");

        Assert.True(FileOps.Execute(new[] { source }, destinationDir.FullName, DropAction.Move, silent: true));
        Assert.False(File.Exists(source));
        Assert.Equal("payload", File.ReadAllText(Path.Combine(destinationDir.FullName, Path.GetFileName(source))));
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void Shell_execute_to_copies_to_the_explicit_renamed_path()
    {
        var sourceDir = Directory.CreateDirectory(Path.Combine(_root, "source folder"));
        var destinationDir = Directory.CreateDirectory(Path.Combine(_root, "destination folder"));
        var source = Path.Combine(sourceDir.FullName, "original.txt");
        var destination = Path.Combine(destinationDir.FullName, "переименованная копия.txt");
        File.WriteAllText(source, "payload");

        Assert.True(FileOps.ExecuteTo(new[] { (source, destination) }, DropAction.Copy, silent: true));
        Assert.Equal("payload", File.ReadAllText(source));
        Assert.Equal("payload", File.ReadAllText(destination));
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void Shell_execute_to_creates_a_missing_destination_parent()
    {
        var source = Path.Combine(_root, "source.txt");
        var destination = Path.Combine(_root, "missing", "nested", "renamed.txt");
        File.WriteAllText(source, "payload");

        var result = FileOps.ExecuteToDetailed(
            new[] { (source, destination) },
            DropAction.Copy,
            silent: true);

        Assert.Equal(FileOperationStatus.Succeeded, result.Status);
        Assert.Equal("payload", File.ReadAllText(destination));
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void Shell_keep_both_reports_the_actual_collision_renamed_path()
    {
        var sourceDir = Directory.CreateDirectory(Path.Combine(_root, "source"));
        var destinationDir = Directory.CreateDirectory(Path.Combine(_root, "destination"));
        var source = Path.Combine(sourceDir.FullName, "report.txt");
        var occupied = Path.Combine(destinationDir.FullName, "report.txt");
        File.WriteAllText(source, "new");
        File.WriteAllText(occupied, "old");

        var result = FileOps.ExecuteDetailed(
            new[] { source }, destinationDir.FullName, DropAction.Copy, policy: ConflictPolicy.KeepBoth);

        Assert.Equal(FileOperationStatus.Succeeded, result.Status);
        var change = Assert.Single(result.UndoableChanges);
        Assert.NotEqual(occupied, change.Destination);
        Assert.Equal("new", File.ReadAllText(change.Destination));
        Assert.Equal("old", File.ReadAllText(occupied));
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void MoveWithoutOverwrite_skips_a_cross_volume_directory_without_copying_or_deleting_it()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "cross-volume folder"));
        var sourceFile = Path.Combine(source.FullName, "payload.txt");
        File.WriteAllText(sourceFile, "payload");
        var destinationRoot = TryCreateRootOnAnotherVolume(_root);
        if (destinationRoot is null)
            return;
        var destination = Path.Combine(destinationRoot, source.Name);

        try
        {
            Assert.False(FileOps.MoveWithoutOverwrite(source.FullName, destinationRoot));
            Assert.True(Directory.Exists(source.FullName));
            Assert.Equal("payload", File.ReadAllText(sourceFile));
            Assert.False(Directory.Exists(destination));
        }
        finally
        {
            TempDir.Delete(destinationRoot);
        }
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void MoveWithoutOverwrite_does_not_merge_a_cross_volume_directory_conflict()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "conflicting folder"));
        var sourceOnly = Path.Combine(source.FullName, "source-only.txt");
        File.WriteAllText(sourceOnly, "source");
        var destinationRoot = TryCreateRootOnAnotherVolume(_root);
        if (destinationRoot is null)
            return;
        var destination = Directory.CreateDirectory(Path.Combine(destinationRoot, source.Name));
        var existing = Path.Combine(destination.FullName, "existing.txt");
        File.WriteAllText(existing, "existing");

        try
        {
            Assert.False(FileOps.MoveWithoutOverwrite(source.FullName, destinationRoot));
            Assert.Equal("source", File.ReadAllText(sourceOnly));
            Assert.Equal("existing", File.ReadAllText(existing));
            Assert.False(File.Exists(Path.Combine(destination.FullName, "source-only.txt")));
        }
        finally
        {
            TempDir.Delete(destinationRoot);
        }
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void MoveWithoutOverwrite_does_not_traverse_a_cross_volume_directory_link()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "linked folder"));
        var external = Directory.CreateDirectory(Path.Combine(_root, "outside source"));
        var externalFile = Path.Combine(external.FullName, "external.txt");
        File.WriteAllText(externalFile, "external");
        var link = Path.Combine(source.FullName, "link");
        CreateJunction(link, external.FullName);

        var destinationRoot = TryCreateRootOnAnotherVolume(_root);
        if (destinationRoot is null)
            return;
        var destination = Path.Combine(destinationRoot, source.Name);
        try
        {
            Assert.True(File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint));
            Assert.False(FileOps.MoveWithoutOverwrite(source.FullName, destinationRoot));
            Assert.True(Directory.Exists(source.FullName));
            Assert.True(Directory.Exists(link));
            Assert.Equal("external", File.ReadAllText(externalFile));
            Assert.False(Directory.Exists(destination));
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            TempDir.Delete(destinationRoot);
        }
    }

    private static void CreateJunction(string junction, string target)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(junction);
        startInfo.ArgumentList.Add(target);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start cmd.exe to create a test junction.");
        if (!process.WaitForExit(5_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Timed out while creating a test junction.");
        }
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Could not create a test junction: {process.StandardError.ReadToEnd().Trim()}");
    }

    private static string? TryCreateRootOnAnotherVolume(string sourcePath)
    {
        var sourceRoot = Path.GetPathRoot(sourcePath);
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady
                || drive.DriveType != DriveType.Fixed
                || string.Equals(drive.RootDirectory.FullName, sourceRoot, StringComparison.OrdinalIgnoreCase))
                continue;

            var candidate = Path.Combine(
                drive.RootDirectory.FullName,
                "dw_cross_volume_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(candidate);
                return candidate;
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        return null;
    }
}
