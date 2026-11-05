#requires -Version 7.2
<#
.SYNOPSIS
    Build the desktop app, generate isolated demo media under %TEMP%, and capture
    real 1600x900 store screenshots in zh-CN, ja-JP and en-US.

.DESCRIPTION
    The script never reads or writes the user's real music library or %APPDATA%\Melotag.
    Each language run uses its own MELOTAG_DATA_DIR and a generated sine-wave demo
    library under %TEMP%\MelotagStoreScreenshots. The demo metadata, lyrics and
    cover art are fictional. No online search is triggered.

    Screenshots are captured from the client area of each top-level window and are
    exactly 1600x900 (2-frame stable capture, cursor parked outside the window).

.PARAMETER OutputRoot
    Output folder. Default: packaging/store/images/screenshots.

.PARAMETER Languages
    Optional subset of zh-CN, ja-JP, en-US for local debugging.

.PARAMETER SkipBuild
    Reuse the existing Release build.

.PARAMETER KeepTemp
    Keep the generated demo/media folder under %TEMP% for inspection.

.PARAMETER Trailer
    Record a raw 1760x990@30fps screen capture of the real app instead of
    saving screenshots. Uses only -Languages[0], writes raw.mp4 and beats.json
    under packaging\store\trailer\<lang>\, and leaves final encoding to
    Build-StoreTrailer.py.

.PARAMETER TrailerRoot
    Output folder for -Trailer. Default: packaging/store/trailer.
#>
[CmdletBinding()]
param(
    [string]$OutputRoot = '',
    [string[]]$Languages = @('zh-CN', 'ja-JP', 'en-US'),
    [switch]$SkipBuild,
    [switch]$KeepTemp,
    [switch]$Trailer,
    [string]$TrailerRoot = ''
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$script:Width = 1600
$script:Height = 900
$script:WindowX = 160
$script:WindowY = 80
$script:TempRoot = Join-Path $env:TEMP 'MelotagStoreScreenshots'
$script:RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $script:RepoRoot 'packaging\store\images\screenshots'
}
$script:OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
if ([string]::IsNullOrWhiteSpace($TrailerRoot)) {
    $TrailerRoot = Join-Path $script:RepoRoot 'packaging\store\trailer'
}
$script:TrailerRoot = [System.IO.Path]::GetFullPath($TrailerRoot)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public sealed class TopWindowInfo
{
    public IntPtr Handle;
    public string Title;
    public bool Visible;
    public bool Enabled;
    public uint ProcessId;
}

public static class CapWin
{
    public const int SW_RESTORE = 9;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const int HWND_TOPMOST = -1;
    public const int HWND_NOTOPMOST = -2;
    public const uint GA_ROOT = 2;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;
    public const uint WM_CLOSE = 0x0010;
    public const int VK_ESCAPE = 0x1B;

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int command);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extraInfo);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(ref POINT point);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extraInfo);

    public struct RECT { public int Left, Top, Right, Bottom; }
    public struct POINT { public int X, Y; }

    public static void EnableDpiAwareness()
    {
        try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
    }

    public static string TitleOf(IntPtr hWnd)
    {
        var sb = new StringBuilder(1024);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static List<TopWindowInfo> Windows(uint processId)
    {
        var list = new List<TopWindowInfo>();
        EnumWindows((h, l) =>
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid != processId) return true;
            list.Add(new TopWindowInfo
            {
                Handle = h,
                Title = TitleOf(h),
                Visible = IsWindowVisible(h),
                Enabled = IsWindowEnabled(h),
                ProcessId = pid,
            });
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static IntPtr FindWindow(uint processId, string title)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) =>
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid != processId || !IsWindowVisible(h)) return true;
            if (TitleOf(h) == title)
            {
                found = h;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static bool ClientSize(IntPtr hWnd, out int width, out int height)
    {
        RECT rect;
        if (!GetClientRect(hWnd, out rect))
        {
            width = height = 0;
            return false;
        }
        width = rect.Right - rect.Left;
        height = rect.Bottom - rect.Top;
        return true;
    }

    public static bool ClientOrigin(IntPtr hWnd, out int x, out int y)
    {
        POINT point = new POINT { X = 0, Y = 0 };
        if (!ClientToScreen(hWnd, ref point))
        {
            x = y = 0;
            return false;
        }
        x = point.X;
        y = point.Y;
        return true;
    }

    public static bool SetClientSize(IntPtr hWnd, int width, int height, int x, int y)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            RECT client, outer;
            GetClientRect(hWnd, out client);
            int currentWidth = client.Right - client.Left;
            int currentHeight = client.Bottom - client.Top;
            if (currentWidth == width && currentHeight == height)
                return true;

            GetWindowRect(hWnd, out outer);
            int outerWidth = outer.Right - outer.Left;
            int outerHeight = outer.Bottom - outer.Top;

            SetWindowPos(
                hWnd,
                IntPtr.Zero,
                x,
                y,
                outerWidth + (width - currentWidth),
                outerHeight + (height - currentHeight),
                SWP_NOZORDER | SWP_SHOWWINDOW);
        }

        RECT final;
        GetClientRect(hWnd, out final);
        return (final.Right - final.Left) == width && (final.Bottom - final.Top) == height;
    }

    public static void SetTopMost(IntPtr hWnd, bool topMost)
    {
        SetWindowPos(
            hWnd,
            new IntPtr(topMost ? HWND_TOPMOST : HWND_NOTOPMOST),
            0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW | (topMost ? 0u : (uint)0x0002));
    }

    public static void BringToFront(IntPtr hWnd)
    {
        ShowWindow(hWnd, SW_RESTORE);
        SetWindowPos(hWnd, new IntPtr(HWND_TOPMOST), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        BringWindowToTop(hWnd);
        ForceForeground(hWnd);
    }

    public static void ForceForeground(IntPtr hWnd)
    {
        IntPtr foreground = GetForegroundWindow();
        uint foregroundThread = 0;
        if (foreground != IntPtr.Zero)
            foregroundThread = GetWindowThreadProcessId(foreground, out uint ignored);
        uint currentThread = GetCurrentThreadId();
        bool attached = false;
        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
                attached = AttachThreadInput(currentThread, foregroundThread, true);
            SetForegroundWindow(hWnd);
            BringWindowToTop(hWnd);
        }
        finally
        {
            if (attached)
                AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    public static bool ForegroundForProcess(uint processId)
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;
        GetWindowThreadProcessId(foreground, out uint pid);
        return pid == processId;
    }

    public static bool RootWindowAtClientPoint(IntPtr hWnd, int clientX, int clientY)
    {
        POINT point = new POINT { X = clientX, Y = clientY };
        if (!ClientToScreen(hWnd, ref point)) return false;
        IntPtr hit = WindowFromPoint(point);
        if (hit == IntPtr.Zero) return false;
        return GetAncestor(hit, GA_ROOT) == hWnd;
    }

    public static void MoveCursor(int x, int y)
    {
        SetCursorPos(x, y);
    }

    public static void ClickAt(int x, int y)
    {
        SetCursorPos(x, y);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
    }

    public static void MouseDown() { mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); }
    public static void MouseUp() { mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); }
    public static void ScrollWheel(int delta) { mouse_event(0x0800, 0, 0, delta, UIntPtr.Zero); }
    public static void PageDown()
    {
        keybd_event(0x22, 0, 0, UIntPtr.Zero);
        keybd_event(0x22, 0, 2, UIntPtr.Zero);
    }

    public static void SendEscape(IntPtr hWnd)
    {
        PostMessage(hWnd, WM_KEYDOWN, new IntPtr(VK_ESCAPE), IntPtr.Zero);
        PostMessage(hWnd, WM_KEYUP, new IntPtr(VK_ESCAPE), IntPtr.Zero);
    }

    public static void SendClose(IntPtr hWnd)
    {
        PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    public static int ScreenWidth() { return GetSystemMetrics(0); }
    public static int ScreenHeight() { return GetSystemMetrics(1); }
    public static uint SystemDpi() { return GetDpiForSystem(); }
    public static uint WindowDpi(IntPtr hWnd) { return GetDpiForWindow(hWnd); }
}
'@

[void][CapWin]::EnableDpiAwareness()

# ---------------------------------------------------------------------------
# Logging helpers
# ---------------------------------------------------------------------------

function Write-Step([string]$Message) {
    Write-Host ("==> " + $Message) -ForegroundColor Cyan
}

function Write-Info([string]$Message) {
    Write-Host ("    " + $Message)
}

# ---------------------------------------------------------------------------
# UIA helpers
# ---------------------------------------------------------------------------

function Get-UiaRoot([IntPtr]$Hwnd) {
    return [System.Windows.Automation.AutomationElement]::FromHandle($Hwnd)
}

function Find-UiaByName {
    param(
        [Parameter(Mandatory)] $Root,
        [Parameter(Mandatory)] [string]$Name,
        $ControlType = $null
    )

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    $elements = $Root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
    for ($i = 0; $i -lt $elements.Count; $i++) {
        $element = $elements.Item($i)
        if ($null -eq $ControlType -or $element.Current.ControlType -eq $ControlType) {
            return $element
        }
    }
    return $null
}

function Find-UiaByAutomationId {
    param(
        [Parameter(Mandatory)] $Root,
        [Parameter(Mandatory)] [string]$AutomationId,
        $ControlType = $null
    )

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $AutomationId)
    $elements = $Root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
    for ($i = 0; $i -lt $elements.Count; $i++) {
        $element = $elements.Item($i)
        if ($null -eq $ControlType -or $element.Current.ControlType -eq $ControlType) {
            return $element
        }
    }
    return $null
}

function Find-UiaAllByControlType {
    param(
        [Parameter(Mandatory)] $Root,
        [Parameter(Mandatory)] $ControlType
    )

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $ControlType)
    return $Root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Invoke-UiaElement {
    param([Parameter(Mandatory)] $Element)
    $pattern = $Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
}

function Select-UiaItem {
    param([Parameter(Mandatory)] $Element)
    $pattern = $Element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    $pattern.Select()
}

function Get-UiaValue {
    param([Parameter(Mandatory)] $Element)
    $pattern = $Element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    return [string]$pattern.Current.Value
}

function Wait-Until {
    param(
        [Parameter(Mandatory)] [scriptblock]$Condition,
        [Parameter(Mandatory)] [string]$Description,
        [int]$TimeoutSeconds = 30,
        [int]$IntervalMilliseconds = 150
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastError = $null
    do {
        try {
            $value = & $Condition
            if ($value) { return $value }
        }
        catch {
            $lastError = $_.Exception.Message
        }
        Start-Sleep -Milliseconds $IntervalMilliseconds
    } while ([DateTime]::UtcNow -lt $deadline)

    if ($lastError) {
        throw "Timed out waiting for $Description. Last error: $lastError"
    }
    throw "Timed out waiting for $Description."
}

function Wait-VisibleWindow {
    param(
        [Parameter(Mandatory)] [int]$ProcessId,
        [Parameter(Mandatory)] [string]$Title,
        [int]$TimeoutSeconds = 30
    )

    return Wait-Until -Description "visible window '$Title'" -TimeoutSeconds $TimeoutSeconds -Condition {
        $handle = [CapWin]::FindWindow([uint32]$ProcessId, $Title)
        if ($handle -eq [IntPtr]::Zero) { return $null }
        $element = Get-UiaRoot $handle
        if ($element.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window) {
            return $handle
        }
        return $null
    }
}

function Wait-WindowGone {
    param(
        [Parameter(Mandatory)] [int]$ProcessId,
        [Parameter(Mandatory)] [string]$Title,
        [int]$TimeoutSeconds = 20
    )

    return Wait-Until -Description "window '$Title' to close" -TimeoutSeconds $TimeoutSeconds -Condition {
        $handle = [CapWin]::FindWindow([uint32]$ProcessId, $Title)
        return ($handle -eq [IntPtr]::Zero)
    }
}

function Assert-NoResourceKeys {
    param(
        [Parameter(Mandatory)] $Root,
        [Parameter(Mandatory)] [string]$Label
    )

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Text)
    $elements = $Root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
    $pattern = '^(?:App|Menu|Toolbar|Dialog|Column|Field|Button|Status|Settings|Rename|Lyrics|Search|Source|TrackStatus|Count|Language|Filter)\.[A-Za-z]'
    for ($i = 0; $i -lt $elements.Count; $i++) {
        $name = $elements.Item($i).Current.Name
        if ($name -match $pattern) {
            throw "Unlocalized resource key visible in $Label`: '$name'"
        }
    }
}

# ---------------------------------------------------------------------------
# Screenshot helpers
# ---------------------------------------------------------------------------

function Get-BitmapHash {
    param([Parameter(Mandatory)] [System.Drawing.Bitmap]$Bitmap)

    $rectangle = New-Object System.Drawing.Rectangle(0, 0, $Bitmap.Width, $Bitmap.Height)
    $data = $Bitmap.LockBits(
        $rectangle,
        [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $length = $data.Stride * $Bitmap.Height
        $bytes = New-Object byte[] $length
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $length)
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try {
            return [Convert]::ToHexString($sha.ComputeHash($bytes))
        }
        finally {
            $sha.Dispose()
        }
    }
    finally {
        $Bitmap.UnlockBits($data)
    }
}

function Get-BitmapStats {
    param([Parameter(Mandatory)] [System.Drawing.Bitmap]$Bitmap)

    $step = 17
    $sum = 0.0
    $sumSquares = 0.0
    $count = 0
    $min = 255
    $max = 0
    $colors = [System.Collections.Generic.HashSet[int]]::new()

    for ($y = 0; $y -lt $Bitmap.Height; $y += $step) {
        for ($x = 0; $x -lt $Bitmap.Width; $x += $step) {
            $color = $Bitmap.GetPixel($x, $y)
            $luma = (0.299 * $color.R) + (0.587 * $color.G) + (0.114 * $color.B)
            $sum += $luma
            $sumSquares += $luma * $luma
            $count++
            if ($luma -lt $min) { $min = $luma }
            if ($luma -gt $max) { $max = $luma }
            [void]$colors.Add($color.ToArgb())
        }
    }

    $mean = $sum / [Math]::Max(1, $count)
    $variance = ($sumSquares / [Math]::Max(1, $count)) - ($mean * $mean)
    return [pscustomobject]@{
        Mean = $mean
        StdDev = [Math]::Sqrt([Math]::Max(0, $variance))
        Min = $min
        Max = $max
        Colors = $colors.Count
    }
}

function New-ClientCapture {
    param(
        [Parameter(Mandatory)] [IntPtr]$Hwnd,
        [int]$Width = $script:Width,
        [int]$Height = $script:Height
    )

    $clientWidth = 0
    $clientHeight = 0
    if (-not [CapWin]::ClientSize($Hwnd, [ref]$clientWidth, [ref]$clientHeight)) {
        throw 'Unable to read the window client size.'
    }
    if ($clientWidth -ne $Width -or $clientHeight -ne $Height) {
        throw "Client size is ${clientWidth}x${clientHeight}, expected ${Width}x${Height}."
    }

    $originX = 0
    $originY = 0
    if (-not [CapWin]::ClientOrigin($Hwnd, [ref]$originX, [ref]$originY)) {
        throw 'Unable to map the client origin to screen coordinates.'
    }

    $bitmap = New-Object System.Drawing.Bitmap($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.CopyFromScreen($originX, $originY, 0, 0, (New-Object System.Drawing.Size($Width, $Height)))
        }
        finally {
            $graphics.Dispose()
        }
        return $bitmap
    }
    catch {
        $bitmap.Dispose()
        throw
    }
}

function Save-StableScreenshot {
    param(
        [Parameter(Mandatory)] [IntPtr]$Hwnd,
        [Parameter(Mandatory)] [int]$ProcessId,
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Label,
        [int]$TimeoutSeconds = 35
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $attempt = 0
    while ([DateTime]::UtcNow -lt $deadline) {
        $attempt++

        [CapWin]::BringToFront($Hwnd)
        [CapWin]::MoveCursor(2, 2)
        Start-Sleep -Milliseconds 240

        $overlayOk = [CapWin]::RootWindowAtClientPoint($Hwnd, 90, $script:Height - 90)
        $foregroundOk = [CapWin]::ForegroundForProcess([uint32]$ProcessId)
        if (-not $overlayOk -or -not $foregroundOk) {
            Start-Sleep -Milliseconds 180
            continue
        }

        $first = $null
        $second = $null
        try {
            $first = New-ClientCapture -Hwnd $Hwnd
            $firstStats = Get-BitmapStats -Bitmap $first
            $firstHash = Get-BitmapHash -Bitmap $first

            Start-Sleep -Milliseconds 140

            $second = New-ClientCapture -Hwnd $Hwnd
            $secondStats = Get-BitmapStats -Bitmap $second
            $secondHash = Get-BitmapHash -Bitmap $second

            $stable = $firstHash -eq $secondHash
            $notBlank = $secondStats.Mean -gt 150 -and
                        $secondStats.StdDev -gt 2 -and
                        $secondStats.Colors -ge 12

            if ($stable -and $notBlank) {
                $directory = [System.IO.Path]::GetDirectoryName($Path)
                [System.IO.Directory]::CreateDirectory($directory) | Out-Null
                $second.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
                Write-Info "Saved $Label -> $Path (attempt $attempt, mean $([Math]::Round($secondStats.Mean, 1)), colors $($secondStats.Colors))"
                return
            }
        }
        finally {
            if ($null -ne $first) { $first.Dispose() }
            if ($null -ne $second) { $second.Dispose() }
        }

        Start-Sleep -Milliseconds 180
    }

    throw "Screenshot '$Label' did not become stable and non-blank within ${TimeoutSeconds}s."
}

function Resize-TargetWindow {
    param(
        [Parameter(Mandatory)] [IntPtr]$Hwnd,
        [Parameter(Mandatory)] [string]$Label
    )

    if (-not [CapWin]::SetClientSize($Hwnd, $script:Width, $script:Height, $script:WindowX, $script:WindowY)) {
        $clientWidth = 0
        $clientHeight = 0
        [void][CapWin]::ClientSize($Hwnd, [ref]$clientWidth, [ref]$clientHeight)
        throw "Could not resize $Label to $($script:Width)x$($script:Height); actual client is ${clientWidth}x${clientHeight}."
    }

    [CapWin]::BringToFront($Hwnd)
    Start-Sleep -Milliseconds 450

    $clientWidth = 0
    $clientHeight = 0
    [void][CapWin]::ClientSize($Hwnd, [ref]$clientWidth, [ref]$clientHeight)
    if ($clientWidth -ne $script:Width -or $clientHeight -ne $script:Height) {
        throw "Unexpected $Label client size after resize: ${clientWidth}x${clientHeight}."
    }
    Write-Info "Client size for $Label is ${clientWidth}x${clientHeight}."
}

# ---------------------------------------------------------------------------
# Demo content
# ---------------------------------------------------------------------------

$lyricsZh = @'
[00:00.00]夜航星图
[00:06.00]林间回声
[00:12.00]
[00:18.50]风把夜色折成纸船
[00:25.00]放进没有尽头的银河
[00:31.50]灯塔在远处慢慢眨眼
[00:38.00]像是替谁保管着答案
[00:44.50]我沿着微光写下航线
[00:51.00]把沉默留给身后的岸
[00:57.50]
[01:04.00]如果云层忽然散开
[01:10.50]请替我数一数星群
[01:17.00]那些没有名字的光
[01:23.50]正穿过很远的清晨
[01:30.00]我把地图折进口袋
[01:36.50]听见潮汐在耳边回答
[01:43.00]
[01:49.50]夜航的人不问归期
[01:56.00]只把方向交给风
[02:02.50]等所有的灯都亮起
[02:09.00]我会在星图尽头等你
[02:15.50]
[02:22.00]风把夜色折成纸船
[02:28.50]放进没有尽头的银河
[02:35.00]灯塔在远处慢慢眨眼
[02:41.50]像是替谁保管着答案
[02:48.00]
[02:54.50]如果云层忽然散开
[03:01.00]请替我数一数星群
[03:07.50]那些没有名字的光
[03:14.00]正穿过很远的清晨
'@

$lyricsJa = @'
[00:00.00]夜航の星図
[00:06.00]森のこだま
[00:12.00]
[00:18.50]風が夜を紙の船に折る
[00:25.00]果てのない銀河へ
[00:31.50]灯台が遠くでまたたく
[00:38.00]誰かの答えを預かるように
[00:44.50]微かな光で航路を描く
[00:51.00]沈黙は岸に置いて
[00:57.50]
[01:04.00]雲がふいに晴れたら
[01:10.50]星の数を数えて
[01:17.00]名前のない光たちが
[01:23.50]遠い朝を抜けてゆく
[01:30.00]地図をポケットにしまい
[01:36.50]潮騒が耳元で答える
[01:43.00]
[01:49.50]夜航びとは帰りを問わない
[01:56.00]風に方角を任せて
[02:02.50]すべての灯がともるころ
[02:09.00]星図の果てで待っている
[02:15.50]
[02:22.00]風が夜を紙の船に折る
[02:28.50]果てのない銀河へ
[02:35.00]灯台が遠くでまたたく
[02:41.50]誰かの答えを預かるように
[02:48.00]
[02:54.50]雲がふいに晴れたら
[03:01.00]星の数を数えて
[03:07.50]名前のない光たちが
[03:14.00]遠い朝を抜けてゆく
'@

$lyricsEn = @'
[00:00.00]Night Chart
[00:06.00]Forest Echo
[00:12.00]
[00:18.50]The wind folds night into a paper boat
[00:25.00]And sets it on an endless river of stars
[00:31.50]A lighthouse blinks somewhere far away
[00:38.00]As if it kept an answer safe for us
[00:44.50]I draw a course along the faintest light
[00:51.00]And leave my silence on the shore
[00:57.50]
[01:04.00]If the clouds should open wide
[01:10.50]Count the constellations for me
[01:17.00]All those lights without a name
[01:23.50]Are passing through a distant morning
[01:30.00]I fold the map into my coat
[01:36.50]And hear the tide reply
[01:43.00]
[01:49.50]Night sailors never ask for home
[01:56.00]They give their compass to the wind
[02:02.50]When every lamp is finally lit
[02:09.00]I will wait at the edge of the chart
[02:15.50]
[02:22.00]The wind folds night into a paper boat
[02:28.50]And sets it on an endless river of stars
[02:35.00]A lighthouse blinks somewhere far away
[02:41.50]As if it kept an answer safe for us
[02:48.00]
[02:54.50]If the clouds should open wide
[03:01.00]Count the constellations for me
[03:07.50]All those lights without a name
[03:14.00]Are passing through a distant morning
'@

$catalog = @{
    'zh-CN' = [pscustomobject]@{
        Language = 'zh-Hans'
        Comment = '虚构演示曲目，仅用于界面展示'
        Lyrics = $lyricsZh
        Albums = @(
            [pscustomobject]@{ Name = '星港夜话'; Artist = '林间回声'; Year = 2024; Genre = '氛围流行'; Composer = '苏澜'; Titles = @('夜航星图','潮汐备忘录','风经过旧车站','第七个夏天','云端邮局','纸飞机航线') },
            [pscustomobject]@{ Name = '潮汐备忘录'; Artist = '苏澜'; Year = 2023; Genre = '独立流行'; Composer = '顾青'; Titles = @('月光便利店','城市慢速','海盐与晚风','山谷回声','玻璃窗上的雨','凌晨四点的灯') },
            [pscustomobject]@{ Name = '山谷来信'; Artist = '夏屿'; Year = 2025; Genre = '民谣'; Composer = '夏屿'; Titles = @('远方的明信片','橘子汽水','白色耳机','无人岛屿','星河入梦') },
            [pscustomobject]@{ Name = '城市慢速'; Artist = '夜航电台'; Year = 2022; Genre = '电子'; Composer = '陈默'; Titles = @('逆光散步','安静的烟火','南风知意','时间的褶皱','归途有风') }
        )
    }
    'ja-JP' = [pscustomobject]@{
        Language = 'ja'
        Comment = '架空のデモ曲目（UI 表示用）'
        Lyrics = $lyricsJa
        Albums = @(
            [pscustomobject]@{ Name = '星港の夜話'; Artist = '森のこだま'; Year = 2024; Genre = 'アンビエントポップ'; Composer = '森川ゆう'; Titles = @('夜航の星図','潮騒のメモ','風の旧駅','七番目の夏','雲の郵便局','紙飛行機の航路') },
            [pscustomobject]@{ Name = '潮騒のメモ'; Artist = '水瀬かなた'; Year = 2023; Genre = 'インディーポップ'; Composer = '水瀬かなた'; Titles = @('月光コンビニ','ゆっくりな街','海塩と夕風','谷のこだま','窓辺の雨','午前四時の灯り') },
            [pscustomobject]@{ Name = '谷からの手紙'; Artist = '朝凪ひかり'; Year = 2025; Genre = 'フォーク'; Composer = '朝凪ひかり'; Titles = @('遠いポストカード','オレンジソーダ','白いイヤホン','無人島','星河の夢') },
            [pscustomobject]@{ Name = '街のスロウ'; Artist = '夜航ラジオ'; Year = 2022; Genre = 'エレクトロニック'; Composer = '夜航ラジオ'; Titles = @('逆光散歩','静かな花火','南風の便り','時間のひだ','帰り道の風') }
        )
    }
    'en-US' = [pscustomobject]@{
        Language = 'en'
        Comment = 'Fictional demo track for UI preview'
        Lyrics = $lyricsEn
        Albums = @(
            [pscustomobject]@{ Name = 'Night Harbor'; Artist = 'Forest Echo'; Year = 2024; Genre = 'Ambient Pop'; Composer = 'Owen Reed'; Titles = @('Night Chart','Tide Notes','Old Station','Summer Seven','Cloud Post','Paper Plane') },
            [pscustomobject]@{ Name = 'Tide Notes'; Artist = 'Lana Su'; Year = 2023; Genre = 'Indie Pop'; Composer = 'Lana Su'; Titles = @('Moon Store','Slow City','Sea Salt','Valley Echo','Window Rain','4 A.M. Light') },
            [pscustomobject]@{ Name = 'Letters from the Valley'; Artist = 'Mira Hale'; Year = 2025; Genre = 'Folk'; Composer = 'Mira Hale'; Titles = @('Far Postcard','Orange Soda','Headphones','Empty Isle','Star River') },
            [pscustomobject]@{ Name = 'Slow City'; Artist = 'Night Radio'; Year = 2022; Genre = 'Electronic'; Composer = 'Night Radio'; Titles = @('Backlight','Fireworks','South Wind','Folded Hours','Way Home') }
        )
    }
}

$locales = @(
    [pscustomobject]@{
        Code = 'zh-CN'; Language = 'zh-Hans'
        MainTitle = '音签 Melotag · 标签编辑器'
        LyricsButton = '歌词'; RenameButton = '重命名'; SettingsButton = '设置'
        LyricsTitle = '歌词'; RenameTitle = '重命名 / 文件名相关'; SettingsTitle = '设置'
        Cancel = '取消'; UiTab = '界面'; LanguageLabel = '中文'
    },
    [pscustomobject]@{
        Code = 'ja-JP'; Language = 'ja'
        MainTitle = 'Melotag · タグエディター'
        LyricsButton = '歌詞'; RenameButton = '名前変更'; SettingsButton = '設定'
        LyricsTitle = '歌詞'; RenameTitle = '名前変更 / ファイル名'; SettingsTitle = '設定'
        Cancel = 'キャンセル'; UiTab = 'インターフェース'; LanguageLabel = '日本語'
    },
    [pscustomobject]@{
        Code = 'en-US'; Language = 'en'
        MainTitle = 'Melotag · Tag Editor'
        LyricsButton = 'Lyrics'; RenameButton = 'Rename'; SettingsButton = 'Settings'
        LyricsTitle = 'Lyrics'; RenameTitle = 'Rename / File Names'; SettingsTitle = 'Settings'
        Cancel = 'Cancel'; UiTab = 'Interface'; LanguageLabel = 'English'
    }
)

$selectedLocales = @()
foreach ($requested in $Languages) {
    $match = $locales | Where-Object { $_.Code -eq $requested }
    if ($null -eq $match) { throw "Unknown language '$requested'. Use zh-CN, ja-JP or en-US." }
    $selectedLocales += $match
}

$palettes = @(
    @{ A = '#0B1E3A'; B = '#3C8D8A'; Accent = '#F2C14E' },
    @{ A = '#3A1C3D'; B = '#B05C6E'; Accent = '#F4D06F' },
    @{ A = '#10312B'; B = '#7FA37A'; Accent = '#E9C46A' },
    @{ A = '#1C1F2B'; B = '#5C6B8A'; Accent = '#E76F51' },
    @{ A = '#2B2D42'; B = '#8D99AE'; Accent = '#EF8354' },
    @{ A = '#0F2027'; B = '#2C5364'; Accent = '#A8DADC' }
)

function Get-SafeFileName([string]$Name) {
    $safe = $Name -replace '[\\/:*?"<>|]', '_'
    return $safe.Trim().TrimEnd('.')
}

function New-CoverSvg {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [int]$Variant
    )

    $palette = $palettes[$Variant % $palettes.Count]
    $cx = 690 + (($Variant * 23) % 60)
    $cy = 230 + (($Variant * 31) % 90)
    $radius = 150 + (($Variant * 13) % 50)
    $ring = $radius + 68
    $line1 = 800 - (($Variant * 37) % 190)
    $line2 = 520 - (($Variant * 29) % 150)
    $line3 = 380 - (($Variant * 17) % 110)

    $svg = @"
<svg xmlns="http://www.w3.org/2000/svg" width="1000" height="1000" viewBox="0 0 1000 1000">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="$($palette.A)"/>
      <stop offset="1" stop-color="$($palette.B)"/>
    </linearGradient>
  </defs>
  <rect width="1000" height="1000" fill="url(#bg)"/>
  <circle cx="$cx" cy="$cy" r="$radius" fill="$($palette.Accent)" opacity="0.84"/>
  <circle cx="$cx" cy="$cy" r="$ring" fill="none" stroke="#FFFFFF" stroke-opacity="0.24" stroke-width="2"/>
  <circle cx="$($cx - 410)" cy="$($cy + 300)" r="112" fill="#FFFFFF" opacity="0.07"/>
  <path d="M120 770 H$line1" stroke="#FFFFFF" stroke-opacity="0.55" stroke-width="3"/>
  <path d="M120 808 H$line2" stroke="#FFFFFF" stroke-opacity="0.28" stroke-width="2"/>
  <path d="M120 838 H$line3" stroke="#FFFFFF" stroke-opacity="0.16" stroke-width="2"/>
</svg>
"@

    [System.IO.File]::WriteAllText($Path, $svg, [System.Text.UTF8Encoding]::new($false))
}

function Invoke-External {
    param(
        [Parameter(Mandatory)] [string]$FileName,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$Description
    )

    $output = & $FileName @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        $text = ($output | Select-Object -Last 12) -join [Environment]::NewLine
        throw "$Description failed (exit $LASTEXITCODE).`n$text"
    }
}

function New-DemoLibrary {
    param(
        [Parameter(Mandatory)] [string]$Ffmpeg,
        [Parameter(Mandatory)] [string]$LocaleCode
    )

    $catalogEntry = $catalog[$LocaleCode]
    $languageRoot = Join-Path $script:TempRoot (Join-Path 'media' $LocaleCode)
    [System.IO.Directory]::CreateDirectory($languageRoot) | Out-Null

    $coverPaths = @()
    $languageIndex = @('zh-CN', 'ja-JP', 'en-US').IndexOf($LocaleCode)
    for ($albumIndex = 0; $albumIndex -lt $catalogEntry.Albums.Count; $albumIndex++) {
        $coverSvg = Join-Path $languageRoot ("cover-$('{0:D2}' -f ($albumIndex + 1)).svg")
        $coverPng = Join-Path $languageRoot ("cover-$('{0:D2}' -f ($albumIndex + 1)).png")
        New-CoverSvg -Path $coverSvg -Variant ($languageIndex * 2 + $albumIndex)
        Invoke-External -FileName $Ffmpeg -Description 'Cover rendering' -Arguments @(
            '-hide_banner', '-loglevel', 'error', '-y',
            '-i', $coverSvg,
            '-frames:v', '1',
            '-pix_fmt', 'rgb24',
            $coverPng
        )
        $coverPaths += $coverPng
    }

    $tracks = [System.Collections.Generic.List[object]]::new()
    $globalIndex = 0
    for ($albumIndex = 0; $albumIndex -lt $catalogEntry.Albums.Count; $albumIndex++) {
        $album = $catalogEntry.Albums[$albumIndex]
        for ($trackIndex = 0; $trackIndex -lt $album.Titles.Count; $trackIndex++) {
            $globalIndex++
            $title = $album.Titles[$trackIndex]
            $trackNumber = $trackIndex + 1
            $fileName = "{0:D2} - {1}.mp3" -f $globalIndex, (Get-SafeFileName $title)
            $filePath = Join-Path $languageRoot $fileName
            $duration = if ($globalIndex -eq 1) { 190 } else { 30 + (($globalIndex * 11) % 28) }
            $frequency = [Math]::Round(220.0 * [Math]::Pow(2.0, (($globalIndex % 12) / 12.0)), 2)
            $lyrics = if ($globalIndex -eq 1) { $catalogEntry.Lyrics } else { '' }

            $ffmpegArguments = [System.Collections.Generic.List[string]]::new()
            $ffmpegArguments.AddRange([string[]]@(
                '-hide_banner', '-loglevel', 'error', '-y', '-nostdin',
                '-f', 'lavfi', '-i', "sine=frequency=${frequency}:sample_rate=44100:duration=$duration",
                '-i', $coverPaths[$albumIndex],
                '-map', '0:a', '-map', '1:v',
                '-c:a', 'libmp3lame', '-b:a', '128k', '-ar', '44100', '-ac', '1',
                '-af', "volume=0.16,afade=t=in:st=0:d=1.2,afade=t=out:st=$($duration - 2):d=2",
                '-c:v', 'copy', '-disposition:v', 'attached_pic',
                '-id3v2_version', '3',
                '-metadata:s:v', 'title=Album cover',
                '-metadata:s:v', 'comment=Cover (front)',
                '-metadata', "title=$title",
                '-metadata', "artist=$($album.Artist)",
                '-metadata', "album=$($album.Name)",
                '-metadata', "album_artist=$($album.Artist)",
                '-metadata', "date=$($album.Year)",
                '-metadata', "track=$trackNumber",
                '-metadata', 'disc=1',
                '-metadata', "genre=$($album.Genre)",
                '-metadata', "composer=$($album.Composer)",
                '-metadata', "comment=$($catalogEntry.Comment)"
            ))
            if (-not [string]::IsNullOrWhiteSpace($lyrics)) {
                $ffmpegArguments.Add('-metadata')
                $ffmpegArguments.Add("lyrics=$lyrics")
            }
            $ffmpegArguments.Add($filePath)

            Invoke-External -FileName $Ffmpeg -Description "Audio generation for $fileName" -Arguments $ffmpegArguments.ToArray()

            $tracks.Add([pscustomobject]@{
                Path = $filePath
                Title = $title
                Album = $album.Name
                Artist = $album.Artist
            })
        }
    }

    return [pscustomobject]@{
        Root = $languageRoot
        Tracks = $tracks
        FirstTitle = $tracks[0].Title
        Lyrics = $catalogEntry.Lyrics
    }
}

function Get-AppVersion {
    $props = Join-Path $script:RepoRoot 'Directory.Build.props'
    if (Test-Path -LiteralPath $props) {
        $match = [regex]::Match(
            [System.IO.File]::ReadAllText($props),
            '<Version>\s*([^<\s]+)\s*</Version>')
        if ($match.Success) { return $match.Groups[1].Value }
    }
    return ''
}

function New-IsolatedSettings {
    param(
        [Parameter(Mandatory)] [string]$DataRoot,
        [Parameter(Mandatory)] [string]$Language
    )

    [System.IO.Directory]::CreateDirectory($DataRoot) | Out-Null
    $settings = [ordered]@{
        SettingsVersion = 5
        Search = [ordered]@{
            UseOnlyFileName = $false
            UseArtist = $true
            UseAlbum = $true
            ItemsPerSource = 10
            Threads = 4
            AutoMatchThreads = 2
        }
        Sources = [ordered]@{
            Tags = @('netease','qq','kugou','kuwo','musicbrainz','itunes','lastfm')
            Pictures = @('netease','kugou','kuwo','itunes','coverart')
            Lyrics = @('netease','qq','kugou','lrclib')
            ConnectorsArtists = ' / '
            ConnectorsLyricAndTranslation = "`n"
            LastFmApiKey = ''
            ITunesCountry = 'CN'
            UserAgent = 'Melotag/1.2.1 (https://github.com/melotag/melotag)'
        }
        Lyrics = [ordered]@{
            ReformatTimeTags = $true
            RemoveTimeTags = $false
            DeleteBlankLines = $true
            DeleteHeadTags = $true
            DownloadTranslation = $false
            DontDownloadLyricWithInstrumentInTitle = $true
            SaveDirectory = ''
            FileNameFormat = '{title}'
            Encoding = 'utf-8'
        }
        Pictures = [ordered]@{
            MaxSizeKb = 1024
            MaxWidth = 1200
            MaxHeight = 1200
            AllowedFormats = @('jpg','png')
            OverwriteEditor = $true
        }
        Writing = [ordered]@{
            Id3v2Version = '2.3'
            KeepModifiedTime = $true
            WriteLyricsToTag = $true
            CommentTagWrite163Key = $false
        }
        Ui = [ordered]@{
            Language = $Language
            CheckForUpdatesOnStartup = $false
            UseCompactRows = $true
            Theme = 'Light'
            WindowWidth = 1600
            WindowHeight = 900
            # Suppress the one-time "what's new" dialog; on a fresh data dir the
            # app otherwise shows it over the toolbar and covers the capture.
            LastSeenVersion = Get-AppVersion
        }
        RecentDirectories = @()
        ManagedDirectories = @()
    }

    $path = Join-Path $DataRoot 'settings.json'
    $json = $settings | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText($path, $json, [System.Text.UTF8Encoding]::new($false))
    return $path
}

function Get-AudioManifest {
    param([Parameter(Mandatory)] [string[]]$Files)
    $lines = foreach ($file in $Files) {
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash
        "$file|$hash"
    }
    return ($lines -join [Environment]::NewLine)
}

# ---------------------------------------------------------------------------
# UI actions
# ---------------------------------------------------------------------------

function Get-MainWindowRoot {
    param([Parameter(Mandatory)] [IntPtr]$Hwnd)
    return Get-UiaRoot $Hwnd
}

function Invoke-ToolbarButton {
    param(
        [Parameter(Mandatory)] [IntPtr]$MainHwnd,
        [Parameter(Mandatory)] [string]$ButtonName,
        [Parameter(Mandatory)] [string]$DialogTitle,
        [Parameter(Mandatory)] [int]$ProcessId
    )

    [CapWin]::BringToFront($MainHwnd)
    [CapWin]::MoveCursor(2, 2)
    Start-Sleep -Milliseconds 220

    $root = Get-MainWindowRoot $MainHwnd
    $button = Find-UiaByName -Root $root -Name $ButtonName -ControlType ([System.Windows.Automation.ControlType]::Button)
    if ($null -eq $button) {
        throw "Toolbar button '$ButtonName' was not found."
    }

    try {
        Invoke-UiaElement -Element $button
    }
    catch {
        Write-Info "UIA invoke failed for '$ButtonName', falling back to a mouse click: $($_.Exception.Message)"
        $rect = $button.Current.BoundingRectangle
        [CapWin]::ClickAt([int]($rect.X + $rect.Width / 2), [int]($rect.Y + $rect.Height / 2))
    }

    [CapWin]::MoveCursor(2, 2)
    return Wait-VisibleWindow -ProcessId $ProcessId -Title $DialogTitle -TimeoutSeconds 30
}

function Close-ModalWindow {
    param(
        [Parameter(Mandatory)] [IntPtr]$DialogHwnd,
        [Parameter(Mandatory)] [string]$CancelName,
        [Parameter(Mandatory)] [string]$DialogTitle,
        [Parameter(Mandatory)] [int]$ProcessId
    )

    $root = Get-UiaRoot $DialogHwnd
    $cancel = Find-UiaByName -Root $root -Name $CancelName -ControlType ([System.Windows.Automation.ControlType]::Button)
    if ($null -ne $cancel) {
        try {
            Invoke-UiaElement -Element $cancel
        }
        catch {
            [CapWin]::SendEscape($DialogHwnd)
        }
    }
    else {
        [CapWin]::SendEscape($DialogHwnd)
    }

    try {
        Wait-WindowGone -ProcessId $ProcessId -Title $DialogTitle -TimeoutSeconds 15 | Out-Null
    }
    catch {
        [CapWin]::SendEscape($DialogHwnd)
        Start-Sleep -Milliseconds 350
        [CapWin]::SendClose($DialogHwnd)
        Wait-WindowGone -ProcessId $ProcessId -Title $DialogTitle -TimeoutSeconds 10 | Out-Null
    }
}

function Wait-ForTracks {
    param(
        [Parameter(Mandatory)] [IntPtr]$MainHwnd,
        [Parameter(Mandatory)] [int]$ExpectedRows
    )

    $grid = Wait-Until -Description 'track DataGrid' -TimeoutSeconds 20 -Condition {
        $root = Get-MainWindowRoot $MainHwnd
        return Find-UiaByAutomationId -Root $root -AutomationId 'TracksGrid'
    }

    $rows = Wait-Until -Description "$ExpectedRows loaded track rows" -TimeoutSeconds 45 -Condition {
        $items = Find-UiaAllByControlType -Root $grid -ControlType ([System.Windows.Automation.ControlType]::DataItem)
        if ($items.Count -eq $ExpectedRows) { return $items }
        return $null
    }

    # The first row is selected automatically. Do not click or focus the grid here:
    # the current-cell focus rectangle looks like a stray black border in a store shot.
    try {
        $selection = $grid.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
        if ($selection.Count -eq 0) {
            Write-Info 'DataGrid reports no selection yet; the title field check will decide.'
        }
    }
    catch {
        # SelectionPattern is optional; the right-panel title check below is authoritative.
    }

    Start-Sleep -Milliseconds 300
}

function Focus-ToolbarButton {
    param(
        [Parameter(Mandatory)] [IntPtr]$MainHwnd,
        [Parameter(Mandatory)] [string]$ButtonName
    )

    $root = Get-MainWindowRoot $MainHwnd
    $button = Find-UiaByName -Root $root -Name $ButtonName -ControlType ([System.Windows.Automation.ControlType]::Button)
    if ($null -ne $button) {
        try { $button.SetFocus() } catch { }
        Start-Sleep -Milliseconds 250
    }
}

function Get-RightPanelEdits {
    param(
        [Parameter(Mandatory)] [IntPtr]$MainHwnd
    )

    $originX = 0
    $originY = 0
    [void][CapWin]::ClientOrigin($MainHwnd, [ref]$originX, [ref]$originY)
    $root = Get-MainWindowRoot $MainHwnd
    $edits = Find-UiaAllByControlType -Root $root -ControlType ([System.Windows.Automation.ControlType]::Edit)
    $right = @()
    for ($i = 0; $i -lt $edits.Count; $i++) {
        $element = $edits.Item($i)
        $rect = $element.Current.BoundingRectangle
        if ($rect.Width -gt 80 -and
            $rect.X -gt ($originX + $script:Width - 380) -and
            $rect.Y -gt ($originY + 220)) {
            $right += [pscustomobject]@{ Element = $element; Top = $rect.Y; Left = $rect.X }
        }
    }
    return ($right | Sort-Object Top, Left)
}

function Open-LyricsDialog {
    param(
        [Parameter(Mandatory)] $Locale,
        [Parameter(Mandatory)] [IntPtr]$MainHwnd,
        [Parameter(Mandatory)] [int]$ProcessId,
        [Parameter(Mandatory)] $Demo
    )

    $dialogHwnd = Invoke-ToolbarButton -MainHwnd $MainHwnd -ButtonName $Locale.LyricsButton -DialogTitle $Locale.LyricsTitle -ProcessId $ProcessId
    Resize-TargetWindow -Hwnd $dialogHwnd -Label 'lyrics dialog'
    Start-Sleep -Milliseconds 500

    $dialogRoot = Get-UiaRoot $dialogHwnd
    $editor = Wait-Until -Description 'lyrics editor' -TimeoutSeconds 15 -Condition {
        $root = Get-UiaRoot $dialogHwnd
        return Find-UiaByAutomationId -Root $root -AutomationId 'Editor'
    }
    $editorText = Get-UiaValue -Element $editor
    $lyricsLines = @($Demo.Lyrics -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $firstLine = $lyricsLines[0]
    $lastLine = $lyricsLines[$lyricsLines.Count - 1]
    if (-not $editorText.Contains($firstLine) -or -not $editorText.Contains($lastLine)) {
        throw "Lyrics dialog does not show the bundled synced lyrics for $($Locale.Code)."
    }

    $resultCombo = Find-UiaByAutomationId -Root $dialogRoot -AutomationId 'ResultCombo'
    if ($null -ne $resultCombo) {
        try {
            $selection = $resultCombo.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
            if ($selection.Count -ne 0) {
                throw "Lyrics search result list is not empty for $($Locale.Code); an online search may have run."
            }
        }
        catch [System.Management.Automation.MethodInvocationException] {
            # No selection pattern is fine; the important assertion is the editor content.
        }
    }

    Assert-NoResourceKeys -Root $dialogRoot -Label 'lyrics dialog'
    $path = Join-Path $script:OutputRoot (Join-Path $Locale.Code '02-lyrics.png')
    Save-StableScreenshot -Hwnd $dialogHwnd -ProcessId $ProcessId -Path $path -Label "lyrics dialog ($($Locale.Code))"

    Close-ModalWindow -DialogHwnd $dialogHwnd -CancelName $Locale.Cancel -DialogTitle $Locale.LyricsTitle -ProcessId $ProcessId
    Start-Sleep -Milliseconds 350
}

function Open-RenameDialog {
    param(
        [Parameter(Mandatory)] $Locale,
        [Parameter(Mandatory)] [IntPtr]$MainHwnd,
        [Parameter(Mandatory)] [int]$ProcessId,
        [Parameter(Mandatory)] [int]$ExpectedRows
    )

    $dialogHwnd = Invoke-ToolbarButton -MainHwnd $MainHwnd -ButtonName $Locale.RenameButton -DialogTitle $Locale.RenameTitle -ProcessId $ProcessId
    Resize-TargetWindow -Hwnd $dialogHwnd -Label 'rename dialog'
    Start-Sleep -Milliseconds 500

    $dialogRoot = Get-UiaRoot $dialogHwnd
    $patternBox = Find-UiaByAutomationId -Root $dialogRoot -AutomationId 'PatternBox'
    if ($null -eq $patternBox -or [string]::IsNullOrWhiteSpace((Get-UiaValue -Element $patternBox))) {
        throw "Rename pattern is empty for $($Locale.Code)."
    }

    $preview = Wait-Until -Description 'rename preview rows' -TimeoutSeconds 15 -Condition {
        $root = Get-UiaRoot $dialogHwnd
        $list = Find-UiaByAutomationId -Root $root -AutomationId 'PreviewList'
        if ($null -eq $list) { return $null }
        $items = Find-UiaAllByControlType -Root $list -ControlType ([System.Windows.Automation.ControlType]::ListItem)
        if ($items.Count -gt 0) { return $items }
        return $null
    }
    Write-Info "Rename preview shows $($preview.Count) row(s) for $ExpectedRows track(s)."

    $message = Find-UiaByAutomationId -Root $dialogRoot -AutomationId 'Message'
    if ($null -ne $message -and [string]::IsNullOrWhiteSpace($message.Current.Name)) {
        throw "Rename preview summary is empty for $($Locale.Code)."
    }

    Assert-NoResourceKeys -Root $dialogRoot -Label 'rename dialog'
    $path = Join-Path $script:OutputRoot (Join-Path $Locale.Code '03-rename.png')
    Save-StableScreenshot -Hwnd $dialogHwnd -ProcessId $ProcessId -Path $path -Label "rename dialog ($($Locale.Code))"

    # Never click Apply; only cancel.
    Close-ModalWindow -DialogHwnd $dialogHwnd -CancelName $Locale.Cancel -DialogTitle $Locale.RenameTitle -ProcessId $ProcessId
    Start-Sleep -Milliseconds 350
}

function Open-SettingsDialog {
    param(
        [Parameter(Mandatory)] $Locale,
        [Parameter(Mandatory)] [IntPtr]$MainHwnd,
        [Parameter(Mandatory)] [int]$ProcessId
    )

    $dialogHwnd = Invoke-ToolbarButton -MainHwnd $MainHwnd -ButtonName $Locale.SettingsButton -DialogTitle $Locale.SettingsTitle -ProcessId $ProcessId
    Resize-TargetWindow -Hwnd $dialogHwnd -Label 'settings dialog'
    Start-Sleep -Milliseconds 400

    $dialogRoot = Get-UiaRoot $dialogHwnd
    $uiTab = Wait-Until -Description "settings tab '$($Locale.UiTab)'" -TimeoutSeconds 15 -Condition {
        $root = Get-UiaRoot $dialogHwnd
        return Find-UiaByName -Root $root -Name $Locale.UiTab -ControlType ([System.Windows.Automation.ControlType]::TabItem)
    }
    Select-UiaItem -Element $uiTab
    Start-Sleep -Milliseconds 500

    $dialogRoot = Get-UiaRoot $dialogHwnd
    $languageBox = Wait-Until -Description 'settings language ComboBox' -TimeoutSeconds 15 -Condition {
        $root = Get-UiaRoot $dialogHwnd
        return Find-UiaByAutomationId -Root $root -AutomationId 'LanguageBox'
    }

    $selectedText = Find-UiaByName -Root $languageBox -Name $Locale.LanguageLabel -ControlType ([System.Windows.Automation.ControlType]::Text)
    if ($null -eq $selectedText) {
        Write-Info "Could not read the selected language label '$($Locale.LanguageLabel)' from the ComboBox."
    }

    $expand = $languageBox.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    [void](Wait-Until -Description 'language drop-down options' -TimeoutSeconds 10 -Condition {
        $root = Get-UiaRoot $dialogHwnd
        $items = Find-UiaAllByControlType -Root $root -ControlType ([System.Windows.Automation.ControlType]::ListItem)
        if ($items.Count -ge 4) { return $items }
        return $null
    })
    Start-Sleep -Milliseconds 450

    Assert-NoResourceKeys -Root $dialogRoot -Label 'settings dialog'
    $path = Join-Path $script:OutputRoot (Join-Path $Locale.Code '04-settings.png')
    Save-StableScreenshot -Hwnd $dialogHwnd -ProcessId $ProcessId -Path $path -Label "settings dialog ($($Locale.Code))"

    try { $expand.Collapse() } catch { }
    Start-Sleep -Milliseconds 250
    Close-ModalWindow -DialogHwnd $dialogHwnd -CancelName $Locale.Cancel -DialogTitle $Locale.SettingsTitle -ProcessId $ProcessId
    Start-Sleep -Milliseconds 350
}

# ---------------------------------------------------------------------------
# Validation / lifecycle
# ---------------------------------------------------------------------------

function Test-CapturedPng {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [int]$Width,
        [Parameter(Mandatory)] [int]$Height
    )

    $result = [pscustomobject]@{
        Path = $Path
        Exists = $false
        Header = $false
        Width = 0
        Height = 0
        Bytes = 0
        Mean = 0
        StdDev = 0
        Colors = 0
        Ok = $false
    }

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $result }
    $result.Exists = $true

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $result.Bytes = $bytes.Length
    $signature = [byte[]](0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)
    $headerOk = $bytes.Length -ge 8
    if ($headerOk) {
        for ($i = 0; $i -lt $signature.Length; $i++) {
            if ($bytes[$i] -ne $signature[$i]) { $headerOk = $false; break }
        }
    }
    $result.Header = $headerOk

    $bitmap = $null
    try {
        $bitmap = [System.Drawing.Bitmap]::FromFile($Path)
        $result.Width = $bitmap.Width
        $result.Height = $bitmap.Height
        $stats = Get-BitmapStats -Bitmap $bitmap
        $result.Mean = [Math]::Round($stats.Mean, 1)
        $result.StdDev = [Math]::Round($stats.StdDev, 2)
        $result.Colors = $stats.Colors
    }
    finally {
        if ($null -ne $bitmap) { $bitmap.Dispose() }
    }

    $result.Ok = $result.Exists -and $result.Header -and
                 $result.Width -eq $Width -and $result.Height -eq $Height -and
                 $result.Bytes -gt 10000 -and
                 $result.Mean -gt 150 -and $result.StdDev -gt 2 -and $result.Colors -ge 12
    return $result
}

function Stop-StaleCaptureProcesses {
    try {
        $processes = Get-CimInstance Win32_Process -Filter "Name='Melotag.exe'" -ErrorAction SilentlyContinue
        foreach ($process in $processes) {
            if ($null -ne $process.CommandLine -and $process.CommandLine.Contains($script:TempRoot)) {
                Write-Info "Stopping stale capture process $($process.ProcessId)."
                Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
            }
        }
    }
    catch {
        Write-Info "Could not inspect stale processes: $($_.Exception.Message)"
    }
}

function Stop-CaptureApp {
    param([System.Diagnostics.Process]$Process)

    if ($null -eq $Process) { return }
    try {
        if (-not $Process.HasExited) {
            $handle = [IntPtr]$Process.MainWindowHandle
            if ($handle -ne [IntPtr]::Zero) {
                [void]$Process.CloseMainWindow()
            }
            if (-not $Process.WaitForExit(5000)) {
                $Process.Kill($true)
                [void]$Process.WaitForExit(3000)
            }
        }
    }
    catch {
        try { $Process.Kill($true) } catch { }
    }
}

# ---------------------------------------------------------------------------
# Trailer recording (-Trailer)
#
# Records the real app through gdigrab at 1760x990 / 30 fps and writes the
# chapter beats the post-production script uses to time captions. Everything
# else (title cards, captions, ambient bed, 1080p encode) lives in
# Build-StoreTrailer.py so a take can be re-edited without re-recording.
# ---------------------------------------------------------------------------

$script:TrailerWidth = 1760
$script:TrailerHeight = 990
$script:TrailerX = 76
$script:TrailerY = 22
$script:TrailerFps = 30
# Approximate ffmpeg start-up before the first captured frame; added to every
# beat so caption timing lines up with the encoded video.
$script:RecordInsetSec = 0.8
$script:Beats = [System.Collections.Generic.List[object]]::new()
$script:BeatClock = $null
$script:RecorderLogTask = $null

function Add-Beat([string]$Name) {
    $t = [Math]::Round($script:BeatClock.Elapsed.TotalSeconds + $script:RecordInsetSec, 3)
    $script:Beats.Add([pscustomobject]@{ name = $Name; t = $t })
    Write-Info ("beat {0,-9} @ {1,6:N2}s" -f $Name, $t)
}

function Move-CursorSmooth {
    param([int]$ToX, [int]$ToY, [int]$Steps = 24, [int]$StepDelayMs = 11)

    $point = New-Object CapWin+POINT
    [void][CapWin]::GetCursorPos([ref]$point)
    $startX = $point.X
    $startY = $point.Y
    for ($i = 1; $i -le $Steps; $i++) {
        $t = $i / [double]$Steps
        $ease = $t * $t * (3 - 2 * $t)
        [void][CapWin]::SetCursorPos(
            [int][Math]::Round($startX + ($ToX - $startX) * $ease),
            [int][Math]::Round($startY + ($ToY - $startY) * $ease))
        Start-Sleep -Milliseconds $StepDelayMs
    }
}

function Click-Point {
    param([int]$X, [int]$Y, [int]$Steps = 24)

    Move-CursorSmooth -ToX $X -ToY $Y -Steps $Steps
    Start-Sleep -Milliseconds 110
    [CapWin]::MouseDown()
    Start-Sleep -Milliseconds 90
    [CapWin]::MouseUp()
    Start-Sleep -Milliseconds 160
}

function Click-Element {
    param([Parameter(Mandatory)]$Element, [int]$Steps = 24)

    $rect = $Element.Current.BoundingRectangle
    if ($rect.Width -le 0 -or $rect.Height -le 0) {
        throw "Cannot click element '$($Element.Current.Name)': empty bounds."
    }
    Click-Point -X ([int]($rect.X + $rect.Width / 2)) -Y ([int]($rect.Y + $rect.Height / 2)) -Steps $Steps
}

function Set-TrailerWindow {
    param(
        [Parameter(Mandatory)][IntPtr]$Hwnd,
        [Parameter(Mandatory)][string]$Label,
        # Avalonia window templates draw rounded corners and a soft edge inside
        # the client rect (about 22 px). Overscan makes that margin fall outside
        # the recording region so no sliver of the window behind shows through.
        [int]$Overscan = 0
    )

    $width = $script:TrailerWidth + 2 * $Overscan
    $height = $script:TrailerHeight + 2 * $Overscan
    $x = $script:TrailerX - $Overscan
    $y = $script:TrailerY - $Overscan

    if (-not [CapWin]::SetClientSize($Hwnd, $width, $height, $x, $y)) {
        throw "Could not resize $Label to ${width}x${height}."
    }
    [CapWin]::BringToFront($Hwnd)
    Start-Sleep -Milliseconds 450

    $clientWidth = 0
    $clientHeight = 0
    [void][CapWin]::ClientSize($Hwnd, [ref]$clientWidth, [ref]$clientHeight)
    if ($clientWidth -ne $width -or $clientHeight -ne $height) {
        throw "Unexpected $Label client size: ${clientWidth}x${clientHeight}, expected ${width}x${height}."
    }
}

function Start-TrailerRecording {
    param([Parameter(Mandatory)][IntPtr]$Hwnd, [Parameter(Mandatory)][string]$Path)

    $originX = 0
    $originY = 0
    if (-not [CapWin]::ClientOrigin($Hwnd, [ref]$originX, [ref]$originY)) {
        throw 'Unable to map the client origin for recording.'
    }
    Write-Info "Recording region: ${originX},${originY} $($script:TrailerWidth)x$($script:TrailerHeight)"

    $ffmpeg = (Get-Command ffmpeg -ErrorAction Stop).Source
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $ffmpeg
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @(
            '-hide_banner', '-loglevel', 'warning', '-y',
            '-f', 'gdigrab', '-framerate', "$($script:TrailerFps)", '-draw_mouse', '1',
            '-offset_x', "$originX", '-offset_y', "$originY",
            '-video_size', "$($script:TrailerWidth)x$($script:TrailerHeight)",
            '-i', 'desktop',
            '-c:v', 'libx264', '-preset', 'veryfast', '-crf', '14',
            '-pix_fmt', 'yuv420p', '-g', "$($script:TrailerFps)",
            '-movflags', '+faststart',
            $Path)) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) { throw 'Could not start ffmpeg for recording.' }
    $script:RecorderLogTask = $process.StandardError.ReadToEndAsync()
    return $process
}

function Stop-TrailerRecording {
    param([System.Diagnostics.Process]$Process, [string]$LogPath)

    if ($null -eq $Process) { return }
    try {
        $Process.StandardInput.WriteLine('q')
        $Process.StandardInput.Flush()
        $Process.StandardInput.Close()
    }
    catch { }
    if (-not $Process.WaitForExit(20000)) {
        try { $Process.Kill($true) } catch { }
        [void]$Process.WaitForExit(3000)
    }
    if ($null -ne $script:RecorderLogTask) {
        $log = $script:RecorderLogTask.Result
        if (-not [string]::IsNullOrWhiteSpace($log)) {
            [System.IO.File]::WriteAllText($LogPath, $log, [System.Text.UTF8Encoding]::new($false))
        }
    }
    $script:RecorderLogTask = $null
}

function Open-TrailerDialog {
    param(
        [Parameter(Mandatory)][IntPtr]$MainHwnd,
        [Parameter(Mandatory)][string]$ButtonName,
        [Parameter(Mandatory)][string]$DialogTitle,
        [Parameter(Mandatory)][int]$ProcessId
    )

    $root = Get-UiaRoot $MainHwnd
    $button = Find-UiaByName -Root $root -Name $ButtonName -ControlType ([System.Windows.Automation.ControlType]::Button)
    if ($null -eq $button) { throw "Toolbar button '$ButtonName' was not found." }

    Click-Element -Element $button -Steps 22
    $dialogHwnd = Wait-VisibleWindow -ProcessId $ProcessId -Title $DialogTitle -TimeoutSeconds 30
    Set-TrailerWindow -Hwnd $dialogHwnd -Label "$DialogTitle window" -Overscan 30
    Start-Sleep -Milliseconds 450
    return $dialogHwnd
}

function Close-TrailerDialog {
    param(
        [Parameter(Mandatory)][IntPtr]$DialogHwnd,
        [Parameter(Mandatory)][string]$CancelName,
        [Parameter(Mandatory)][string]$DialogTitle,
        [Parameter(Mandatory)][int]$ProcessId
    )

    $root = Get-UiaRoot $DialogHwnd
    $cancel = Find-UiaByName -Root $root -Name $CancelName -ControlType ([System.Windows.Automation.ControlType]::Button)
    if ($null -ne $cancel) {
        Click-Element -Element $cancel -Steps 20
    }
    else {
        [CapWin]::SendEscape($DialogHwnd)
    }

    try {
        Wait-WindowGone -ProcessId $ProcessId -Title $DialogTitle -TimeoutSeconds 15 | Out-Null
    }
    catch {
        [CapWin]::SendEscape($DialogHwnd)
        Start-Sleep -Milliseconds 300
        [CapWin]::SendClose($DialogHwnd)
        Wait-WindowGone -ProcessId $ProcessId -Title $DialogTitle -TimeoutSeconds 10 | Out-Null
    }
}

function Wait-SelectedTitle {
    param(
        [Parameter(Mandatory)][IntPtr]$MainHwnd,
        [Parameter(Mandatory)][string]$Expected,
        [int]$TimeoutSeconds = 6
    )

    return Wait-Until -Description "selected title '$Expected'" -TimeoutSeconds $TimeoutSeconds -Condition {
        $edits = Find-UiaAllByControlType -Root (Get-UiaRoot $MainHwnd) -ControlType ([System.Windows.Automation.ControlType]::Edit)
        for ($i = 0; $i -lt $edits.Count; $i++) {
            if ((Get-UiaValue -Element $edits.Item($i)) -eq $Expected) { return $true }
        }
        return $false
    }
}

function Invoke-TrailerTour {
    param(
        [Parameter(Mandatory)]$Locale,
        [Parameter(Mandatory)][IntPtr]$MainHwnd,
        [Parameter(Mandatory)][int]$ProcessId
    )

    $root = Get-UiaRoot $MainHwnd
    $grid = Wait-Until -Description 'track DataGrid' -TimeoutSeconds 20 -Condition {
        return Find-UiaByAutomationId -Root (Get-UiaRoot $MainHwnd) -AutomationId 'TracksGrid'
    }
    $rows = Find-UiaAllByControlType -Root $grid -ControlType ([System.Windows.Automation.ControlType]::DataItem)
    if ($rows.Count -lt 4) { throw "Expected at least 4 track rows, found $($rows.Count)." }

    $originX = 0
    $originY = 0
    [void][CapWin]::ClientOrigin($MainHwnd, [ref]$originX, [ref]$originY)

    # Park the cursor on the status bar so the take starts with a still frame.
    [void][CapWin]::SetCursorPos($originX + 1520, $originY + 950)
    Start-Sleep -Milliseconds 350

    $script:Beats.Clear()
    $script:BeatClock = [System.Diagnostics.Stopwatch]::StartNew()

    Add-Beat 'import'
    Start-Sleep -Milliseconds 1500

    # Select a different track: the editor panel updates with its album/cover.
    $row = $rows.Item(3).Current.BoundingRectangle
    Click-Point -X ([int]($row.X + 260)) -Y ([int]($row.Y + $row.Height / 2)) -Steps 26
    [void](Wait-SelectedTitle -MainHwnd $MainHwnd -Expected $Demo.Tracks[3].Title)
    Add-Beat 'select'
    Start-Sleep -Milliseconds 2200

    # Back to the first track; it is the one carrying synced lyrics.
    $row = $rows.Item(0).Current.BoundingRectangle
    Click-Point -X ([int]($row.X + 260)) -Y ([int]($row.Y + $row.Height / 2)) -Steps 20
    [void](Wait-SelectedTitle -MainHwnd $MainHwnd -Expected $Demo.Tracks[0].Title)
    Start-Sleep -Milliseconds 750

    $lyricsHwnd = Open-TrailerDialog -MainHwnd $MainHwnd -ButtonName $Locale.LyricsButton -DialogTitle $Locale.LyricsTitle -ProcessId $ProcessId
    $editor = Wait-Until -Description 'lyrics editor' -TimeoutSeconds 15 -Condition {
        return Find-UiaByAutomationId -Root (Get-UiaRoot $lyricsHwnd) -AutomationId 'Editor'
    }
    if ([string]::IsNullOrWhiteSpace((Get-UiaValue -Element $editor))) {
        throw 'Lyrics editor is empty; the take would show a blank panel.'
    }
    Add-Beat 'lyrics'
    Start-Sleep -Milliseconds 2600
    Close-TrailerDialog -DialogHwnd $lyricsHwnd -CancelName $Locale.Cancel -DialogTitle $Locale.LyricsTitle -ProcessId $ProcessId
    Start-Sleep -Milliseconds 350

    $renameHwnd = Open-TrailerDialog -MainHwnd $MainHwnd -ButtonName $Locale.RenameButton -DialogTitle $Locale.RenameTitle -ProcessId $ProcessId
    Add-Beat 'rename'
    Start-Sleep -Milliseconds 2900
    Close-TrailerDialog -DialogHwnd $renameHwnd -CancelName $Locale.Cancel -DialogTitle $Locale.RenameTitle -ProcessId $ProcessId
    Start-Sleep -Milliseconds 350

    $settingsHwnd = Open-TrailerDialog -MainHwnd $MainHwnd -ButtonName $Locale.SettingsButton -DialogTitle $Locale.SettingsTitle -ProcessId $ProcessId
    $uiTab = Wait-Until -Description "settings tab '$($Locale.UiTab)'" -TimeoutSeconds 15 -Condition {
        return Find-UiaByName -Root (Get-UiaRoot $settingsHwnd) -Name $Locale.UiTab -ControlType ([System.Windows.Automation.ControlType]::TabItem)
    }
    Click-Element -Element $uiTab -Steps 18
    Start-Sleep -Milliseconds 550

    $languageBox = Wait-Until -Description 'settings language ComboBox' -TimeoutSeconds 15 -Condition {
        return Find-UiaByAutomationId -Root (Get-UiaRoot $settingsHwnd) -AutomationId 'LanguageBox'
    }
    Add-Beat 'settings'
    Start-Sleep -Milliseconds 700

    $expand = $languageBox.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    Start-Sleep -Milliseconds 2300
    try { $expand.Collapse() } catch { }
    Start-Sleep -Milliseconds 350

    Close-TrailerDialog -DialogHwnd $settingsHwnd -CancelName $Locale.Cancel -DialogTitle $Locale.SettingsTitle -ProcessId $ProcessId
    Start-Sleep -Milliseconds 450

    # Final beat: the whole 22-track library fits on screen, so there is nothing
    # to scroll. Select a mid-list row and hold for the closing caption.
    [CapWin]::BringToFront($MainHwnd)
    [CapWin]::ForceForeground($MainHwnd)
    Start-Sleep -Milliseconds 300
    $row = $rows.Item(7).Current.BoundingRectangle
    Click-Point -X ([int]($row.X + 260)) -Y ([int]($row.Y + $row.Height / 2)) -Steps 18
    [void](Wait-SelectedTitle -MainHwnd $MainHwnd -Expected $Demo.Tracks[7].Title)
    [void][CapWin]::SetCursorPos($originX + 640, $originY + 690)
    Add-Beat 'outro'
    Start-Sleep -Milliseconds 2400

    $script:BeatClock.Stop()
    Write-Info ("take length: {0:N2}s" -f $script:BeatClock.Elapsed.TotalSeconds)
}

function Invoke-TrailerTake {
    param(
        [Parameter(Mandatory)]$Locale,
        [Parameter(Mandatory)]$Demo,
        [Parameter(Mandatory)][string]$DesktopExe,
        [Parameter(Mandatory)][string]$OutputRoot
    )

    $dataRoot = Join-Path $script:TempRoot (Join-Path 'data' ($Locale.Code + '-trailer'))
    if (Test-Path -LiteralPath $dataRoot) {
        Remove-Item -LiteralPath $dataRoot -Recurse -Force
    }
    $settingsPath = New-IsolatedSettings -DataRoot $dataRoot -Language $Locale.Language
    Write-Step "Launching $($Locale.Code) for the trailer take"
    Write-Info "settings.json: $settingsPath"

    $audioPaths = @($Demo.Tracks | ForEach-Object { $_.Path })
    $manifestBefore = Get-AudioManifest -Files $audioPaths

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $DesktopExe
    $startInfo.WorkingDirectory = $Demo.Root
    $startInfo.UseShellExecute = $false
    $startInfo.Environment['MELOTAG_DATA_DIR'] = $dataRoot
    foreach ($audioPath in $audioPaths) {
        [void]$startInfo.ArgumentList.Add($audioPath)
    }

    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) { throw 'Could not start Melotag.' }
    Write-Info "Process id: $($process.Id)"

    $recorder = $null
    $outputDir = Join-Path $OutputRoot $Locale.Code
    [System.IO.Directory]::CreateDirectory($outputDir) | Out-Null
    $rawPath = Join-Path $outputDir 'raw.mp4'
    $beatsPath = Join-Path $outputDir 'beats.json'
    $logPath = Join-Path $outputDir 'recorder.log'
    foreach ($stale in @($rawPath, $beatsPath, $logPath)) {
        if (Test-Path -LiteralPath $stale) { Remove-Item -LiteralPath $stale -Force }
    }

    try {
        $mainHwnd = Wait-VisibleWindow -ProcessId $process.Id -Title $Locale.MainTitle -TimeoutSeconds 60
        Set-TrailerWindow -Hwnd $mainHwnd -Label 'main window'
        Start-Sleep -Milliseconds 900
        $null = Wait-ForTracks -MainHwnd $mainHwnd -ExpectedRows $Demo.Tracks.Count
        Start-Sleep -Milliseconds 500

        # Windows swallows the first click on an inactive window. Activate and
        # focus the app before recording so the first row click really selects.
        $originX = 0
        $originY = 0
        [void][CapWin]::ClientOrigin($mainHwnd, [ref]$originX, [ref]$originY)
        Click-Point -X ($originX + 40) -Y ($originY + $script:TrailerHeight - 16) -Steps 4
        Start-Sleep -Milliseconds 400

        $recorder = Start-TrailerRecording -Hwnd $mainHwnd -Path $rawPath
        Start-Sleep -Milliseconds 700

        Invoke-TrailerTour -Locale $Locale -MainHwnd $mainHwnd -ProcessId $process.Id

        Stop-TrailerRecording -Process $recorder -LogPath $logPath
        $recorder = $null
        Start-Sleep -Milliseconds 300

        $manifestAfter = Get-AudioManifest -Files $audioPaths
        if ($manifestBefore -ne $manifestAfter) {
            throw 'Audio files were modified during the trailer take; rename/apply must never run.'
        }
        Write-Info 'Audio file hashes unchanged.'

        if (-not (Test-Path -LiteralPath $rawPath)) { throw "Recording was not written: $rawPath" }
        $probe = & ffprobe -v error -select_streams v:0 -show_entries stream=width,height,nb_frames -show_entries format=duration -of json $rawPath 2>&1
        if ($LASTEXITCODE -ne 0) { throw "ffprobe failed on $rawPath`n$probe" }
        $info = ($probe -join [Environment]::NewLine) | ConvertFrom-Json
        $stream = $info.streams[0]
        $duration = [double]$info.format.duration
        Write-Info ("raw take: {0}x{1}, {2} frames, {3:N2}s" -f $stream.width, $stream.height, $stream.nb_frames, $duration)
        if ($stream.width -ne $script:TrailerWidth -or $stream.height -ne $script:TrailerHeight) {
            throw "Recording size is $($stream.width)x$($stream.height), expected $($script:TrailerWidth)x$($script:TrailerHeight)."
        }
        if ($duration -lt 12) { throw "Recording is only $duration s; the tour did not complete." }

        $beats = [ordered]@{
            language = $Locale.Code
            width = $script:TrailerWidth
            height = $script:TrailerHeight
            fps = $script:TrailerFps
            duration = [Math]::Round($duration, 3)
            beats = @($script:Beats)
        }
        [System.IO.File]::WriteAllText($beatsPath, ($beats | ConvertTo-Json -Depth 5), [System.Text.UTF8Encoding]::new($false))
        Write-Info "wrote $beatsPath"
    }
    finally {
        if ($null -ne $recorder) {
            Stop-TrailerRecording -Process $recorder -LogPath $logPath
        }
        Stop-CaptureApp -Process $process
        Start-Sleep -Milliseconds 400
    }
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

$success = $false
$captureProcess = $null
$allResults = @()

try {
    Write-Step "Repository: $script:RepoRoot"
    Write-Step "Output:     $script:OutputRoot"
    Write-Step "Temp:       $script:TempRoot"

    $ffmpegCommand = Get-Command ffmpeg -ErrorAction SilentlyContinue
    if ($null -eq $ffmpegCommand) { throw 'ffmpeg is required but was not found on PATH.' }
    $ffmpeg = $ffmpegCommand.Source

    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) { throw 'dotnet is required but was not found on PATH.' }
    $dotnet = $dotnetCommand.Source

    $screenWidth = [CapWin]::ScreenWidth()
    $screenHeight = [CapWin]::ScreenHeight()
    $systemDpi = [CapWin]::SystemDpi()
    Write-Step "Display: ${screenWidth}x${screenHeight} at ${systemDpi} DPI"
    if ($screenWidth -lt 1920 -or $screenHeight -lt 1080) {
        throw "This capture script needs a 1920x1080 (or larger) desktop; found ${screenWidth}x${screenHeight}."
    }
    if ($systemDpi -ne 96) {
        throw "Display scaling must be 100% (96 DPI); found $systemDpi DPI."
    }

    Stop-StaleCaptureProcesses

    if (Test-Path -LiteralPath $script:TempRoot) {
        Remove-Item -LiteralPath $script:TempRoot -Recurse -Force
    }
    [System.IO.Directory]::CreateDirectory($script:TempRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory($script:OutputRoot) | Out-Null

    foreach ($locale in $selectedLocales) {
        if ($Trailer) { break }
        $languageOutput = Join-Path $script:OutputRoot $locale.Code
        if (Test-Path -LiteralPath $languageOutput) {
            Remove-Item -LiteralPath $languageOutput -Recurse -Force
        }
        [System.IO.Directory]::CreateDirectory($languageOutput) | Out-Null
    }

    $desktopProject = Join-Path $script:RepoRoot 'src\Melotag.Desktop\Melotag.Desktop.csproj'
    $desktopExe = Join-Path $script:RepoRoot 'src\Melotag.Desktop\bin\Release\net10.0\Melotag.exe'
    if (-not $SkipBuild) {
        Write-Step 'dotnet clean (Release)'
        & $dotnet clean $desktopProject -c Release -v minimal | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'dotnet clean failed.' }

        Write-Step 'dotnet build (Release)'
        & $dotnet build $desktopProject -c Release -v minimal | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }
    }
    if (-not (Test-Path -LiteralPath $desktopExe -PathType Leaf)) {
        throw "Desktop executable not found: $desktopExe. Run without -SkipBuild first."
    }
    Write-Step "Using $desktopExe"

    $demoLibraries = @{}
    $demoLocales = if ($Trailer) { @($selectedLocales[0]) } else { $selectedLocales }
    foreach ($locale in $demoLocales) {
        Write-Step "Generating fictional demo library for $($locale.Code)"
        $demoLibraries[$locale.Code] = New-DemoLibrary -Ffmpeg $ffmpeg -LocaleCode $locale.Code
        Write-Info "Created $($demoLibraries[$locale.Code].Tracks.Count) tracks under $($demoLibraries[$locale.Code].Root)"
    }

    if ($Trailer) {
        $locale = $selectedLocales[0]
        Write-Step "Trailer take: $($locale.Code) -> $script:TrailerRoot"
        Invoke-TrailerTake -Locale $locale -Demo $demoLibraries[$locale.Code] -DesktopExe $desktopExe -OutputRoot $script:TrailerRoot
        Write-Step 'Raw trailer footage captured. Run Build-StoreTrailer.py to produce the 1080p video.'
        $success = $true
        return
    }

    foreach ($locale in $selectedLocales) {
        $demo = $demoLibraries[$locale.Code]
        $dataRoot = Join-Path $script:TempRoot (Join-Path 'data' $locale.Code)
        if (Test-Path -LiteralPath $dataRoot) {
            Remove-Item -LiteralPath $dataRoot -Recurse -Force
        }
        $settingsPath = New-IsolatedSettings -DataRoot $dataRoot -Language $locale.Language
        Write-Step "Launching $($locale.Code) with isolated data $dataRoot"
        Write-Info "settings.json: $settingsPath"

        $audioPaths = @($demo.Tracks | ForEach-Object { $_.Path })
        $manifestBefore = Get-AudioManifest -Files $audioPaths

        $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = $desktopExe
        $startInfo.WorkingDirectory = $demo.Root
        $startInfo.UseShellExecute = $false
        $startInfo.Environment['MELOTAG_DATA_DIR'] = $dataRoot
        foreach ($audioPath in $audioPaths) {
            [void]$startInfo.ArgumentList.Add($audioPath)
        }

        $captureProcess = [System.Diagnostics.Process]::Start($startInfo)
        if ($null -eq $captureProcess) { throw 'Could not start Melotag.' }
        Write-Info "Process id: $($captureProcess.Id)"

        try {
            $mainHwnd = Wait-VisibleWindow -ProcessId $captureProcess.Id -Title $locale.MainTitle -TimeoutSeconds 60
            Resize-TargetWindow -Hwnd $mainHwnd -Label 'main window'
            Start-Sleep -Milliseconds 900

            $mainRoot = Get-MainWindowRoot $mainHwnd
            $null = Wait-ForTracks -MainHwnd $mainHwnd -ExpectedRows $demo.Tracks.Count

            $titleEdit = Wait-Until -Description 'selected track title field' -TimeoutSeconds 20 -Condition {
                $edits = @(Get-RightPanelEdits -MainHwnd $mainHwnd)
                if ($edits.Count -eq 0) { return $null }
                $value = Get-UiaValue -Element $edits[0].Element
                if ($value -eq $demo.FirstTitle) { return $edits[0].Element }
                return $null
            }
            Write-Info "Selected title: $($demo.FirstTitle)"

            $cover = Wait-Until -Description 'selected track cover preview' -TimeoutSeconds 20 -Condition {
                $originX = 0
                $originY = 0
                [void][CapWin]::ClientOrigin($mainHwnd, [ref]$originX, [ref]$originY)
                $root = Get-MainWindowRoot $mainHwnd
                $images = Find-UiaAllByControlType -Root $root -ControlType ([System.Windows.Automation.ControlType]::Image)
                for ($i = 0; $i -lt $images.Count; $i++) {
                    $rect = $images.Item($i).Current.BoundingRectangle
                    if ($rect.Width -gt 100 -and $rect.X -gt ($originX + $script:Width - 380)) {
                        return $images.Item($i)
                    }
                }
                return $null
            }

            # Move keyboard focus off the DataGrid so its current-cell rectangle does not
            # appear as a black border around the Artist cell.
            Focus-ToolbarButton -MainHwnd $mainHwnd -ButtonName $locale.SettingsButton

            Assert-NoResourceKeys -Root (Get-MainWindowRoot $mainHwnd) -Label 'main window'
            $mainPath = Join-Path $script:OutputRoot (Join-Path $locale.Code '01-main.png')
            Save-StableScreenshot -Hwnd $mainHwnd -ProcessId $captureProcess.Id -Path $mainPath -Label "main window ($($locale.Code))"

            Open-LyricsDialog -Locale $locale -MainHwnd $mainHwnd -ProcessId $captureProcess.Id -Demo $demo
            Open-RenameDialog -Locale $locale -MainHwnd $mainHwnd -ProcessId $captureProcess.Id -ExpectedRows $demo.Tracks.Count
            Open-SettingsDialog -Locale $locale -MainHwnd $mainHwnd -ProcessId $captureProcess.Id

            $manifestAfter = Get-AudioManifest -Files $audioPaths
            if ($manifestBefore -ne $manifestAfter) {
                throw "Audio files were modified for $($locale.Code); rename/apply must never run."
            }
            Write-Info 'Audio file hashes unchanged.'

            $mainHandle = [IntPtr]$captureProcess.MainWindowHandle
            if ($mainHandle -ne [IntPtr]::Zero) { [CapWin]::SetTopMost($mainHandle, $false) }
        }
        finally {
            Stop-CaptureApp -Process $captureProcess
            $captureProcess = $null
            Start-Sleep -Milliseconds 400
        }
    }

    Write-Step 'Validating captured PNG files'
    foreach ($locale in $selectedLocales) {
        foreach ($name in @('01-main.png', '02-lyrics.png', '03-rename.png', '04-settings.png')) {
            $path = Join-Path $script:OutputRoot (Join-Path $locale.Code $name)
            $result = Test-CapturedPng -Path $path -Width $script:Width -Height $script:Height
            $allResults += $result
        }
    }

    foreach ($result in $allResults) {
        $relative = [System.IO.Path]::GetRelativePath($script:OutputRoot, $result.Path)
        Write-Host ("  {0} | {1}x{2} | {3} bytes | header {4} | mean {5} | sd {6} | colors {7} | {8}" -f
            $relative, $result.Width, $result.Height, $result.Bytes, $result.Header,
            $result.Mean, $result.StdDev, $result.Colors, $(if ($result.Ok) { 'OK' } else { 'FAIL' }))
    }

    $failed = @($allResults | Where-Object { -not $_.Ok })
    if ($failed.Count -gt 0) {
        throw "$($failed.Count) screenshot(s) failed validation."
    }

    Write-Step 'All screenshots validated.'
    $success = $true
}
finally {
    if ($null -ne $captureProcess) {
        Stop-CaptureApp -Process $captureProcess
    }

    if ($success -and -not $KeepTemp) {
        try {
            if (Test-Path -LiteralPath $script:TempRoot) {
                Remove-Item -LiteralPath $script:TempRoot -Recurse -Force
            }
            Write-Step 'Temporary media removed.'
        }
        catch {
            Write-Info "Could not remove temp folder: $($_.Exception.Message)"
        }
    }
    else {
        Write-Info "Temp folder kept at: $script:TempRoot"
    }
}

Write-Host ''
Write-Host 'Screenshot set:' -ForegroundColor Green
foreach ($locale in $selectedLocales) {
    Write-Host ("  " + (Join-Path $script:OutputRoot $locale.Code))
}
