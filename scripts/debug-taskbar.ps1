# Diagnoses why the CursorMeter bars are missing from the taskbar.
# Runs on Windows PowerShell 5.1 and PowerShell 7. The simulation needs no display;
# on Windows it also reads the live taskbar and the CursorMeter window.
#Requires -Version 5.1

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Convert-Dip([int]$Dip, [int]$Dpi) {
    if ($Dpi -le 0) { $Dpi = 96 }
    return [int][Math]::Round($Dip * ($Dpi / 96.0))
}

function Get-FitThickness([int]$Thickness, [int]$Dpi) {
    if ($Thickness -le 0) { return (Convert-Dip 40 $Dpi) }
    $fitted = $Thickness - (Convert-Dip 4 $Dpi)
    $min = Convert-Dip 30 $Dpi
    $max = Convert-Dip 48 $Dpi
    if ($fitted -lt $min) { return $min }
    if ($fitted -gt $max) { return $max }
    return $fitted
}

# Same placement the app uses. Rectangles are left, top, right, bottom.
function Get-Slot($Taskbar, $Tray, [int]$Width, [int]$Height) {
    $x = $Tray.Left - 6 - $Width
    $y = $Taskbar.Top + [Math]::Max(0, [int](($Taskbar.Height - $Height) / 2))
    $minX = $Taskbar.Left + 2
    $maxX = [Math]::Max($minX, $Taskbar.Right - $Width - 2)
    $minY = $Taskbar.Top + 2
    $maxY = [Math]::Max($minY, $Taskbar.Bottom - $Height - 2)
    if ($x -lt $minX) { $x = $minX }
    if ($x -gt $maxX) { $x = $maxX }
    if ($y -lt $minY) { $y = $minY }
    if ($y -gt $maxY) { $y = $maxY }
    return [pscustomobject]@{
        Left = $x; Top = $y; Right = $x + $Width; Bottom = $y + $Height
        Width = $Width; Height = $Height
    }
}

function Test-Inside($Slot, $Outer) {
    return $Slot.Left -ge $Outer.Left -and $Slot.Top -ge $Outer.Top -and $Slot.Right -le $Outer.Right -and $Slot.Bottom -le $Outer.Bottom
}

function New-Rect([int]$Left, [int]$Top, [int]$Right, [int]$Bottom) {
    return [pscustomobject]@{
        Left = $Left; Top = $Top; Right = $Right; Bottom = $Bottom
        Width = $Right - $Left; Height = $Bottom - $Top
    }
}

function Show-Simulation {
    Write-Host ''
    Write-Host '=== Simulacao: 1920x1080, relogio a direita ==='
    Write-Host 'A build 26300 (Windows 11 26H2) desenha a barra numa superficie XAML.'
    Write-Host 'Uma janela filha de Shell_TrayWnd recebe WM_PAINT mas, a 100%, nao entra nessa superficie.'
    Write-Host 'A 125% o Windows amplia a janela (o processo nao declarava PerMonitorV2) e ela passa a ver-se.'
    Write-Host ''

    $failed = $false
    foreach ($case in @(
            @{ Name = '100%'; Dpi = 96;  Thickness = 48; Top = 1032 },
            @{ Name = '125%'; Dpi = 120; Thickness = 60; Top = 1020 },
            @{ Name = '150%'; Dpi = 144; Thickness = 72; Top = 1008 }
        )) {
        $bar = New-Rect 0 $case.Top 1920 ($case.Top + $case.Thickness)
        $trayWidth = Convert-Dip 180 $case.Dpi
        $tray = New-Rect ($bar.Right - $trayWidth) $bar.Top $bar.Right $bar.Bottom
        $width = Convert-Dip 252 $case.Dpi
        $height = Get-FitThickness $case.Thickness $case.Dpi
        $slot = Get-Slot $bar $tray $width $height
        $inside = Test-Inside $slot $bar

        # What the old child window did at a fractional scale: Windows scaled a 96-DPI child.
        $hostedHeight = [int][Math]::Round(44 * ($case.Dpi / 96.0))
        $hostedBottom = 2 + $hostedHeight
        $sticksOut = $hostedBottom -gt 48

        Write-Host ("{0,-5} barra {1}px dpi {2}  slot {3},{4} {5}x{6}  dentro={7}  filho a 96dpi ampliado sai da barra de 48={8}" -f `
                $case.Name, $case.Thickness, $case.Dpi, $slot.Left, $slot.Top, $slot.Width, $slot.Height, $inside, $sticksOut)
        if (-not $inside) { $failed = $true }
    }

    Write-Host ''
    Write-Host 'O encaixe novo usa pixeis fisicos da barra e desenha por cima dela, por isso 100%, 125% e 150% ficam dentro.'
    if ($failed) { exit 1 }
}

function Show-LiveProbe {
    if ($env:OS -ne 'Windows_NT') {
        Write-Host ''
        Write-Host 'Sonda ao vivo: esta maquina nao e Windows. No outro PC corre .\scripts\debug-taskbar.ps1'
        return
    }

    Write-Host ''
    Write-Host '=== Sonda ao vivo ==='
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class TaskbarProbe {
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lparam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lparam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr FindWindow(string cls, string name);
    [DllImport("user32.dll")] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int GetAwarenessFromDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(long packed);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    public struct RECT { public int Left, Top, Right, Bottom; }
    public static string ClassOf(IntPtr hwnd) {
        var buf = new StringBuilder(256);
        GetClassName(hwnd, buf, buf.Capacity);
        return buf.ToString();
    }
    public static string TextOf(IntPtr hwnd) {
        var buf = new StringBuilder(512);
        GetWindowText(hwnd, buf, buf.Capacity);
        return buf.ToString();
    }
    public static List<IntPtr> WindowsOf(uint pid) {
        var found = new List<IntPtr>();
        EnumWindows((hwnd, _) => {
            uint owner;
            GetWindowThreadProcessId(hwnd, out owner);
            if (owner == pid) found.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@

    $os = Get-CimInstance Win32_OperatingSystem
    Write-Host ("SO {0}  build {1}  {2}" -f $os.Caption, $os.BuildNumber, $os.Version)

    $taskbar = [TaskbarProbe]::FindWindow('Shell_TrayWnd', $null)
    if ($taskbar -eq [IntPtr]::Zero) {
        Write-Host 'Shell_TrayWnd nao existe. O Explorer nao esta pronto.'
        return
    }

    $bar = New-Object TaskbarProbe+RECT
    [void][TaskbarProbe]::GetWindowRect($taskbar, [ref]$bar)
    $dpi = [TaskbarProbe]::GetDpiForWindow($taskbar)
    $bridge = [TaskbarProbe]::FindWindowEx($taskbar, [IntPtr]::Zero, 'Windows.UI.Composition.DesktopWindowContentBridge', $null)
    $tray = [TaskbarProbe]::FindWindowEx($taskbar, [IntPtr]::Zero, 'TrayNotifyWnd', $null)
    Write-Host ("Barra ({0},{1})-({2},{3})  {4}x{5}  dpi {6}  ponte XAML {7}" -f `
            $bar.Left, $bar.Top, $bar.Right, $bar.Bottom, ($bar.Right - $bar.Left), ($bar.Bottom - $bar.Top), $dpi, ($(if ($bridge -eq [IntPtr]::Zero) { 'ausente' } else { 'presente' })))
    if ($tray -ne [IntPtr]::Zero) {
        $trayRect = New-Object TaskbarProbe+RECT
        [void][TaskbarProbe]::GetWindowRect($tray, [ref]$trayRect)
        Write-Host ("Bandeja ({0},{1})-({2},{3})" -f $trayRect.Left, $trayRect.Top, $trayRect.Right, $trayRect.Bottom)
    }

    $scale = [Math]::Round($dpi / 96.0, 2)
    if ($bridge -ne [IntPtr]::Zero -and $dpi -eq 96) {
        Write-Host 'Veredito da barra: Windows 11 a 100%. Uma janela filha fica por baixo da superficie XAML e nao se ve.'
    }
    elseif ($bridge -ne [IntPtr]::Zero) {
        Write-Host ("Veredito da barra: Windows 11 a {0}%. O ajuste de DPI pode fazer uma janela filha aparecer; a 100% nao aparece." -f ($scale * 100))
    }

    $processes = @(Get-Process -Name 'CursorMeter' -ErrorAction SilentlyContinue)
    if ($processes.Count -eq 0) {
        Write-Host 'CursorMeter nao esta a correr.'
        return
    }

    foreach ($process in $processes) {
        Write-Host ("Processo {0} pid {1}" -f $process.ProcessName, $process.Id)
        $windows = [TaskbarProbe]::WindowsOf([uint32]$process.Id)
        foreach ($hwnd in $windows) {
            $rect = New-Object TaskbarProbe+RECT
            [void][TaskbarProbe]::GetWindowRect($hwnd, [ref]$rect)
            $parent = [TaskbarProbe]::GetParent($hwnd)
            $parentClass = if ($parent -eq [IntPtr]::Zero) { '(nenhum)' } else { [TaskbarProbe]::ClassOf($parent) }
            $style = [TaskbarProbe]::GetWindowLong($hwnd, -16)
            $exStyle = [TaskbarProbe]::GetWindowLong($hwnd, -20)
            $child = ($style -band 0x40000000) -ne 0
            $topmost = ($exStyle -band 0x00000008) -ne 0
            $visible = [TaskbarProbe]::IsWindowVisible($hwnd)
            $awareness = [TaskbarProbe]::GetAwarenessFromDpiAwarenessContext([TaskbarProbe]::GetWindowDpiAwarenessContext($hwnd))
            $awarenessName = switch ($awareness) { 0 { 'unaware' } 1 { 'system' } 2 { 'per-monitor' } default { "$awareness" } }
            $cx = [int](($rect.Left + $rect.Right) / 2)
            $cy = [int](($rect.Top + $rect.Bottom) / 2)
            $packed = ([int64]$cx) -bor (([int64]$cy) -shl 32)
            $hit = [TaskbarProbe]::WindowFromPoint($packed)
            $hitClass = if ($hit -eq [IntPtr]::Zero) { '(nenhum)' } else { [TaskbarProbe]::ClassOf($hit) }
            $onTop = $hit -eq $hwnd
            Write-Host ("  hwnd 0x{0:X} '{1}' classe {2}" -f $hwnd.ToInt64(), [TaskbarProbe]::TextOf($hwnd), [TaskbarProbe]::ClassOf($hwnd))
            Write-Host ("    rect ({0},{1})-({2},{3}) visivel={4} filho={5} topmost={6} dpi={7} pai={8}" -f `
                    $rect.Left, $rect.Top, $rect.Right, $rect.Bottom, $visible, $child, $topmost, $awarenessName, $parentClass)
            Write-Host ("    WindowFromPoint no centro -> {0}  a nossa janela esta por cima={1}" -f $hitClass, $onTop)
            if ($child -and $parentClass -eq 'Shell_TrayWnd' -and -not $onTop) {
                Write-Host '    Veredito: a janela esta dentro da barra mas outra janela cobre o centro. No Windows 11 isso e a superficie XAML.'
            }
            elseif ($topmost -and $onTop) {
                Write-Host '    Veredito: as barras estao por cima da barra de tarefas.'
            }
        }
    }
}

Show-LiveProbe
Show-Simulation
