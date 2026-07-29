[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class CodexQuotaHudIconNative
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr handle);
}
'@

$directory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $directory -Force | Out-Null

$size = 64
$bitmap = [System.Drawing.Bitmap]::new(
    $size,
    $size,
    [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode =
    [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::Transparent)

$background = [System.Drawing.SolidBrush]::new(
    [System.Drawing.Color]::FromArgb(255, 8, 22, 32))
$accent = [System.Drawing.Pen]::new(
    [System.Drawing.Color]::FromArgb(255, 83, 220, 248),
    6)
$accent.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$accent.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$inner = [System.Drawing.Pen]::new(
    [System.Drawing.Color]::FromArgb(180, 83, 220, 248),
    2)
$dot = [System.Drawing.SolidBrush]::new(
    [System.Drawing.Color]::FromArgb(255, 255, 255, 255))

try {
    $graphics.FillEllipse($background, 3, 3, 58, 58)
    $graphics.DrawArc($accent, 8, 8, 48, 48, -90, 302)
    $graphics.DrawEllipse($inner, 17, 17, 30, 30)
    $graphics.FillEllipse($dot, 45, 11, 7, 7)

    $handle = $bitmap.GetHicon()
    try {
        $borrowed = [System.Drawing.Icon]::FromHandle($handle)
        $icon = $borrowed.Clone()
        try {
            $stream = [System.IO.File]::Open(
                $OutputPath,
                [System.IO.FileMode]::Create,
                [System.IO.FileAccess]::Write,
                [System.IO.FileShare]::None)
            try {
                $icon.Save($stream)
            }
            finally {
                $stream.Dispose()
            }
        }
        finally {
            $icon.Dispose()
            $borrowed.Dispose()
        }
    }
    finally {
        [void][CodexQuotaHudIconNative]::DestroyIcon($handle)
    }
}
finally {
    $dot.Dispose()
    $inner.Dispose()
    $accent.Dispose()
    $background.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}
