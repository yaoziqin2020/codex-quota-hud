using System.Diagnostics;
using CodexQuotaHud.App.Infrastructure;

namespace CodexQuotaHud.App.Tests.Infrastructure;

public sealed class InstalledAppLauncherTests
{
    private const string CustomSelectionKey =
        "custom:11111111-1111-1111-1111-111111111111";

    [Fact]
    public void ResolvesCurrentUserInstalledExecutable()
    {
        var launcher = new InstalledAppLauncher(
            localAppData: @"C:\Users\Test\AppData\Local",
            fileExists: _ => true,
            startProcess: _ => true);

        Assert.Equal(
            @"C:\Users\Test\AppData\Local\Programs\CodexQuotaHud\CodexQuotaHud.App.exe",
            launcher.ExecutablePath);
        Assert.True(launcher.IsAvailable);
    }

    [Fact]
    public void TryLaunch_StartsExactAbsolutePathWithoutArguments()
    {
        ProcessStartInfo? captured = null;
        var launcher = new InstalledAppLauncher(
            localAppData: @"C:\Users\Test\AppData\Local",
            fileExists: _ => true,
            startProcess: info =>
            {
                captured = info;
                return true;
            });

        Assert.True(launcher.TryLaunch(out var error));
        Assert.Null(error);
        Assert.Equal(launcher.ExecutablePath, captured!.FileName);
        Assert.Empty(captured.ArgumentList);
        Assert.True(captured.UseShellExecute);
    }

    [Fact]
    public void TryLaunchActivation_UsesOnlyTwoSeparatedCanonicalArguments()
    {
        ProcessStartInfo? captured = null;
        var launcher = new InstalledAppLauncher(
            localAppData: @"C:\Users\Test\AppData\Local",
            fileExists: _ => true,
            startProcess: info =>
            {
                captured = info;
                return true;
            });

        Assert.True(launcher.TryLaunchActivation(CustomSelectionKey, out var error));

        Assert.Null(error);
        Assert.Equal(launcher.ExecutablePath, captured!.FileName);
        Assert.True(Path.IsPathFullyQualified(captured.FileName));
        Assert.True(captured.UseShellExecute);
        Assert.Equal(string.Empty, captured.Arguments);
        Assert.Equal(
            ["--activate-skin", CustomSelectionKey],
            captured.ArgumentList);
    }

    [Theory]
    [InlineData("builtin:HudDial")]
    [InlineData("custom:AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA")]
    [InlineData("custom:11111111111111111111111111111111")]
    public void TryLaunchActivation_InvalidKeyStartsNothing(string selectionKey)
    {
        var starts = 0;
        var launcher = new InstalledAppLauncher(
            @"C:\Present",
            _ => true,
            _ => { starts++; return true; });

        Assert.False(launcher.TryLaunchActivation(selectionKey, out var error));

        Assert.Equal("无效的自定义皮肤选择键", error);
        Assert.Equal(0, starts);
    }

    [Fact]
    public void TryLaunchActivation_MissingFalseAndExceptionsReturnStableErrors()
    {
        var starts = 0;
        var missing = new InstalledAppLauncher(
            @"C:\Missing",
            _ => false,
            _ => { starts++; return true; });
        var falseStart = new InstalledAppLauncher(
            @"C:\Present",
            _ => true,
            _ => false);
        var win32Failure = new InstalledAppLauncher(
            @"C:\Present",
            _ => true,
            _ => throw new System.ComponentModel.Win32Exception("controlled detail"));
        var ioFailure = new InstalledAppLauncher(
            @"C:\Present",
            _ => true,
            _ => throw new IOException("controlled detail"));

        Assert.False(missing.TryLaunchActivation(CustomSelectionKey, out var missingError));
        Assert.False(falseStart.TryLaunchActivation(CustomSelectionKey, out var falseError));
        Assert.False(win32Failure.TryLaunchActivation(CustomSelectionKey, out var win32Error));
        Assert.False(ioFailure.TryLaunchActivation(CustomSelectionKey, out var ioError));

        Assert.Equal("未找到已安装正式版", missingError);
        Assert.Equal("正式版启动失败", falseError);
        Assert.Equal("正式版启动失败", win32Error);
        Assert.Equal("正式版启动失败", ioError);
        Assert.Equal(0, starts);
    }

    [Fact]
    public void MissingOrFailedLaunch_ReturnsErrorWithoutStarting()
    {
        var starts = 0;
        var missing = new InstalledAppLauncher(
            @"C:\Missing",
            _ => false,
            _ => { starts++; return true; });
        var failing = new InstalledAppLauncher(
            @"C:\Present",
            _ => true,
            _ => throw new InvalidOperationException("start failed"));

        Assert.False(missing.TryLaunch(out var missingError));
        Assert.Equal("未找到已安装正式版", missingError);
        Assert.Equal(0, starts);
        Assert.False(failing.TryLaunch(out var startError));
        Assert.Contains("start failed", startError);
    }
}
