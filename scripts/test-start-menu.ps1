<#
.SYNOPSIS
    Testa o que acontece as barras do CursorMeter quando se abre o menu Iniciar
    (e, opcionalmente, a Vista de Tarefas) no Windows 11.

.DESCRIPTION
    Abre e fecha o menu Iniciar com a tecla Windows, mede varias vezes e responde a
    perguntas que uma sonda de posicao nao responde sozinha:

      1. As barras continuam VISIVEIS? Compara uma captura de ecra da zona das barras
         com a captura feita antes de abrir o menu (nao basta ler a posicao da janela).
      2. O recorte da barra de tarefas (1.2.6) foi mesmo aplicado e foi retirado ao fechar?
         Le GetWindowRgn(Shell_TrayWnd) antes, durante e depois.
      3. Em que modo estao as barras? Le a ultima linha "Placement:" do registo da app:
         ilha (Start11), flutuante (Start11 sem espaco para a ilha), corte (1.2.6),
         ao-lado (1.2.5 ou recurso), normal.
      4. As barras saltaram para fora da barra? Compara o retangulo da janela.
      5. Com -TesteCrash: o recorte fica na barra se a app morrer com o menu aberto, e a app
         limpa um recorte antigo no arranque?

    Serve para a 1.2.5 e para a 1.2.6: o relatorio diz em que modo a versao em teste esta.
    Deve ser repetido a 100%, 125% e 150% de escala (use -Etiqueta para separar as pastas),
    e num PC com Start11 e noutro sem Start11.

    Durante o teste o script carrega na tecla Windows. Nao uses o teclado nem o rato.
    Cada execucao cria uma pasta em %LOCALAPPDATA%\CursorMeter\testes\<data>-<etiqueta> com:
      teste.log        tudo o que o script escreveu (ambiente, medidas, resultados)
      cursormeter.log  copia do registo da propria app (linhas "Placement:")
      resultado.md     tabela de resultados
      resultado.json   os mesmos resultados, para ler por programa
      *.png            capturas da zona das barras e do ecra inteiro
    O caminho da ultima execucao fica em %LOCALAPPDATA%\CursorMeter\testes\ultimo.txt.
    Codigo de saida 0 sem falhas, 1 com falhas, 2 se o proprio script deu erro.

    Duplo clique em scripts\test-start-menu.cmd faz o mesmo sem abrir a consola a mao.

.PARAMETER SoAmbiente
    So mostra o ambiente e uma leitura. Nao carrega em nenhuma tecla. Seguro para correr a qualquer altura.

.PARAMETER Observar
    So observa durante -Segundos segundos e escreve cada mudanca. Abre e fecha o menu a mao.

.PARAMETER VistaDeTarefas
    Repete o teste com a Vista de Tarefas (Win+Tab, fecha com Esc).

.PARAMETER TesteCrash
    Termina o CursorMeter com o menu aberto para ver se o recorte fica na barra, volta a abri-lo e
    limpa o recorte no fim. Pede confirmacao, a menos que se use -Sim.

.PARAMETER AbrirJanelas
    Abre N janelas do Bloco de Notas antes do teste e fecha-as no fim. Com o Start11 (e a barra sem agrupar
    janelas) enchem a ilha dos icones e tiram o espaco da ilha das barras: reproduz o modo flutuante.

.PARAMETER Saida
    Pasta onde guardar o log e as capturas. Por omissao uma pasta nova em %LOCALAPPDATA%\CursorMeter\testes.

.PARAMETER Etiqueta
    Texto livre para a pasta de saida, por exemplo 125pct ou start11.

.PARAMETER LimiarPixeis
    Fracao minima de pixeis iguais aos do antes (0 a 1) para dar a captura como igual. Por omissao 0.90.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\test-start-menu.ps1 -Etiqueta 100pct

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\test-start-menu.ps1 -VistaDeTarefas -TesteCrash -Etiqueta 125pct
#>
[CmdletBinding()]
param(
    [switch]$SoAmbiente,
    [switch]$Observar,
    [int]$Segundos = 25,
    [switch]$VistaDeTarefas,
    [switch]$TesteCrash,
    [int]$AbrirJanelas = 0,
    [switch]$Sim,
    [string]$Etiqueta = '',
    [string]$Saida = '',
    [double]$LimiarPixeis = 0.90
)

$ErrorActionPreference = 'Stop'
# Inside a function $PSBoundParameters is the function's own, so keep the script's here for the log header.
$script:BoundArgs = $PSBoundParameters

if (-not ('CursorMeterProbe.Probe' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CursorMeterProbe
{
    public sealed class Reading
    {
        public bool TaskbarFound;
        public int TaskbarBand = -1;
        public int TaskbarL, TaskbarT, TaskbarR, TaskbarB;
        public int Dpi = 96;
        public int RegionKind;
        public int RegionL, RegionT, RegionR, RegionB;
        public int MeterPid;
        public bool WidgetFound;
        public long WidgetHwnd;
        public int WidgetBand = -1;
        public int WidgetL, WidgetT, WidgetR, WidgetB;
        public bool StartOpen;
        public string StartDetail = "";
    }

    public static class Probe
    {
        const int GwlExStyle = -20;
        const int WsExTopmost = 0x00000008;
        const int DwmwaCloaked = 14;
        const uint GwNext = 2;
        const uint GwPrev = 3;

        delegate bool EnumProc(IntPtr hwnd, IntPtr lparam);
        static readonly EnumProc Callback = OnEnum;
        static readonly List<IntPtr> Found = new List<IntPtr>();
        static uint WantPid;

        // Must run before any window API or System.Drawing call, so the rectangles are real pixels.
        public static string MakeDpiAware()
        {
            try
            {
                if (SetProcessDpiAwarenessContext(new IntPtr(-4)))
                    return "per-monitor-v2";
            }
            catch (EntryPointNotFoundException)
            {
            }

            try
            {
                return SetProcessDPIAware() ? "system" : "falhou";
            }
            catch (EntryPointNotFoundException)
            {
                return "falhou";
            }
        }

        public static Reading Measure()
        {
            Reading r = new Reading();
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero)
                return r;

            r.TaskbarFound = true;
            uint band;
            if (GetWindowBand(taskbar, out band))
                r.TaskbarBand = (int)band;
            RECT rect;
            if (GetWindowRect(taskbar, out rect))
            {
                r.TaskbarL = rect.Left;
                r.TaskbarT = rect.Top;
                r.TaskbarR = rect.Right;
                r.TaskbarB = rect.Bottom;
            }

            r.Dpi = DpiOf(taskbar);
            IntPtr region = CreateRectRgn(0, 0, 0, 0);
            if (region != IntPtr.Zero)
            {
                try
                {
                    int kind = GetWindowRgn(taskbar, region);
                    r.RegionKind = kind;
                    RECT box;
                    if (kind > 1 && GetRgnBox(region, out box) != 0)
                    {
                        r.RegionL = box.Left;
                        r.RegionT = box.Top;
                        r.RegionR = box.Right;
                        r.RegionB = box.Bottom;
                    }
                }
                finally
                {
                    DeleteObject(region);
                }
            }

            int pid = FirstPid("CursorMeter");
            r.MeterPid = pid;
            if (pid != 0)
            {
                IntPtr widget = PickWidget(pid);
                if (widget != IntPtr.Zero)
                {
                    r.WidgetFound = true;
                    r.WidgetHwnd = widget.ToInt64();
                    uint widgetBand;
                    if (GetWindowBand(widget, out widgetBand))
                        r.WidgetBand = (int)widgetBand;
                    RECT wr;
                    if (GetWindowRect(widget, out wr))
                    {
                        r.WidgetL = wr.Left;
                        r.WidgetT = wr.Top;
                        r.WidgetR = wr.Right;
                        r.WidgetB = wr.Bottom;
                    }
                }
            }

            string detail;
            r.StartOpen = StartMenuOpen(out detail);
            r.StartDetail = detail;
            return r;
        }

        // Removes any region from the taskbar. Only for cleaning up after -TesteCrash.
        public static bool ClearTaskbarRegion()
        {
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero)
                return false;
            return SetWindowRgn(taskbar, IntPtr.Zero, true) != 0;
        }

        public static void KeyTap(byte key)
        {
            keybd_event(key, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, 2, UIntPtr.Zero);
        }

        public static void KeyChord(byte modifier, byte key)
        {
            keybd_event(modifier, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, 2, UIntPtr.Zero);
            keybd_event(modifier, 0, 2, UIntPtr.Zero);
        }

        public static string Sample()
        {
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero)
                return "motivo=nenhuma  camada=sem-barra  menu=?";

            uint taskbarBand;
            bool taskbarBandOk = GetWindowBand(taskbar, out taskbarBand);
            IntPtr tray = FindTray(taskbar);

            int meterPid = FirstPid("CursorMeter");
            IntPtr widget = IntPtr.Zero;
            string widgetNote = "CursorMeter nao esta a correr";
            uint widgetBand = 0;
            bool widgetBandOk = false;
            string widgetRect = "-";
            if (meterPid != 0)
            {
                widget = PickWidget(meterPid);
                if (widget == IntPtr.Zero)
                    widgetNote = "processo sem janela de barras";
                else
                {
                    widgetBandOk = GetWindowBand(widget, out widgetBand);
                    widgetRect = RectOf(widget);
                    widgetNote = string.Format("0x{0:X}", widget.ToInt64());
                }
            }

            bool same = widget != IntPtr.Zero && (Reaches(widget, taskbar, GwPrev) || Reaches(widget, taskbar, GwNext));
            bool bandDiff = taskbarBandOk && widgetBandOk && taskbarBand != widgetBand && !same;
            string camada;
            if (widget == IntPtr.Zero)
                camada = meterPid == 0 ? "sem-widget" : "sem-janela";
            else if (!taskbarBandOk || !widgetBandOk)
                camada = "api-falhou";
            else
                camada = bandDiff ? "sim" : "nao";

            string menuDetail;
            bool menu = StartMenuOpen(out menuDetail);
            string motivo;
            if (bandDiff && menu) motivo = "ambas";
            else if (bandDiff) motivo = "camada";
            else if (menu) motivo = "menu";
            else motivo = "nenhuma";

            return string.Format(
                "motivo={0}  camada={1}  menu={2}  barraBanda={3}  widgetBanda={4}  cadeia={5}  widget={6} {7}  tray={8}  menuJanela={9}  barra={10}  regiao={11}",
                motivo,
                camada,
                menu ? "sim" : "nao",
                taskbarBandOk ? BandName(taskbarBand) : "?",
                (widget != IntPtr.Zero && widgetBandOk) ? BandName(widgetBand) : "?",
                widget == IntPtr.Zero ? "?" : (same ? "sim" : "nao"),
                widgetNote,
                widgetRect,
                tray == IntPtr.Zero ? "ausente" : RectOf(tray),
                menuDetail,
                RectOf(taskbar),
                RegionText(taskbar));
        }

        public static string BandName(uint band)
        {
            switch (band)
            {
                case 0: return "0:DEFAULT";
                case 1: return "1:DESKTOP";
                case 2: return "2:UIACCESS";
                case 3: return "3:IMMERSIVE_IHM";
                case 4: return "4:IMMERSIVE_NOTIFICATION";
                case 5: return "5:IMMERSIVE_APPCHROME";
                case 6: return "6:IMMERSIVE_MOGO";
                case 12: return "12:IMMERSIVE_BACKGROUND";
                case 13: return "13:IMMERSIVE_SEARCH";
                case 16: return "16:SYSTEM_TOOLS";
                default: return band.ToString();
            }
        }

        static string RegionText(IntPtr window)
        {
            IntPtr region = CreateRectRgn(0, 0, 0, 0);
            if (region == IntPtr.Zero)
                return "?";
            try
            {
                int kind = GetWindowRgn(window, region);
                if (kind == 0)
                    return "nenhuma";
                if (kind == 1)
                    return "vazia";
                RECT box;
                string text = kind == 2 ? "simples" : "complexa";
                if (GetRgnBox(region, out box) != 0)
                    text += string.Format(" caixa={0},{1} {2}x{3}", box.Left, box.Top, box.Right - box.Left, box.Bottom - box.Top);
                return text;
            }
            finally
            {
                DeleteObject(region);
            }
        }

        static int DpiOf(IntPtr window)
        {
            try
            {
                uint dpi = GetDpiForWindow(window);
                return dpi >= 96 ? (int)dpi : 96;
            }
            catch (EntryPointNotFoundException)
            {
                return 96;
            }
        }

        static bool StartMenuOpen(out string detail)
        {
            detail = "nenhuma";
            Process[] processes = Process.GetProcessesByName("StartMenuExperienceHost");
            try
            {
                if (processes.Length == 0)
                {
                    detail = "processo-ausente";
                    return false;
                }

                bool open = false;
                int matches = 0;
                int cloakedLarge = 0;
                string first = "nenhuma";
                for (int i = 0; i < processes.Length; i++)
                {
                    List<IntPtr> windows = WindowsOf((uint)processes[i].Id);
                    for (int w = 0; w < windows.Count; w++)
                    {
                        IntPtr hwnd = windows[w];
                        if (!IsWindowVisible(hwnd))
                            continue;
                        RECT rect;
                        if (!GetWindowRect(hwnd, out rect))
                            continue;
                        int width = rect.Right - rect.Left;
                        int height = rect.Bottom - rect.Top;
                        if (width < 80 || height < 80)
                            continue;
                        int cloaked;
                        bool cloakedOk = DwmGetWindowAttribute(hwnd, DwmwaCloaked, out cloaked, 4) == 0;
                        if (cloakedOk && cloaked != 0)
                        {
                            cloakedLarge++;
                            continue;
                        }
                        matches++;
                        if (!open)
                            first = RectOf(hwnd);
                        open = true;
                    }
                }

                if (open)
                    detail = matches == 1 ? first : first + " x" + matches.ToString();
                else if (cloakedLarge > 0)
                    detail = "so-cloaked " + cloakedLarge.ToString();
                return open;
            }
            finally
            {
                for (int i = 0; i < processes.Length; i++)
                    processes[i].Dispose();
            }
        }

        static IntPtr PickWidget(int pid)
        {
            List<IntPtr> windows = WindowsOf((uint)pid);
            IntPtr best = IntPtr.Zero;
            int bestScore = -1;
            int bestArea = -1;
            for (int i = 0; i < windows.Count; i++)
            {
                IntPtr hwnd = windows[i];
                if (!IsWindowVisible(hwnd))
                    continue;
                RECT rect;
                if (!GetWindowRect(hwnd, out rect))
                    continue;
                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;
                if (width < 40 || height < 16)
                    continue;
                int score = 0;
                if (height >= 20 && height <= 120 && width >= 80 && width <= 800)
                    score += 2;
                IntPtr ex = GetWindowLongPtr(hwnd, GwlExStyle);
                if ((ex.ToInt64() & WsExTopmost) != 0)
                    score += 1;
                int area = width * height;
                if (score > bestScore || (score == bestScore && area > bestArea))
                {
                    best = hwnd;
                    bestScore = score;
                    bestArea = area;
                }
            }
            return best;
        }

        static List<IntPtr> WindowsOf(uint pid)
        {
            Found.Clear();
            WantPid = pid;
            EnumWindows(Callback, IntPtr.Zero);
            return new List<IntPtr>(Found);
        }

        static bool OnEnum(IntPtr hwnd, IntPtr lparam)
        {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid == WantPid)
                Found.Add(hwnd);
            return true;
        }

        static bool Reaches(IntPtr window, IntPtr other, uint direction)
        {
            IntPtr current = window;
            for (int i = 0; i < 4096; i++)
            {
                current = GetWindow(current, direction);
                if (current == IntPtr.Zero)
                    return false;
                if (current == other)
                    return true;
            }
            return false;
        }

        // The published file may be CursorMeter.exe or CursorMeter-1.2.6.exe, so match the prefix.
        static int FirstPid(string prefix)
        {
            Process[] processes = Process.GetProcesses();
            try
            {
                for (int i = 0; i < processes.Length; i++)
                {
                    if (processes[i].ProcessName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        return processes[i].Id;
                }
                return 0;
            }
            finally
            {
                for (int i = 0; i < processes.Length; i++)
                    processes[i].Dispose();
            }
        }

        static IntPtr FindTray(IntPtr taskbar)
        {
            IntPtr direct = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            if (direct != IntPtr.Zero)
                return direct;
            IntPtr child = IntPtr.Zero;
            while ((child = FindWindowEx(taskbar, child, null, null)) != IntPtr.Zero)
            {
                IntPtr nested = FindWindowEx(child, IntPtr.Zero, "TrayNotifyWnd", null);
                if (nested != IntPtr.Zero)
                    return nested;
            }
            return IntPtr.Zero;
        }

        static string RectOf(IntPtr hwnd)
        {
            RECT rect;
            if (!GetWindowRect(hwnd, out rect))
                return "sem-rect";
            return string.Format("{0},{1} {2}x{3}", rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowName);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        static extern bool GetWindowBand(IntPtr hwnd, out uint band);

        [DllImport("user32.dll")]
        static extern IntPtr GetWindow(IntPtr hwnd, uint command);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumProc callback, IntPtr lparam);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        static extern int GetWindowRgn(IntPtr hwnd, IntPtr region);

        [DllImport("user32.dll")]
        static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

        [DllImport("user32.dll")]
        static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

        [DllImport("gdi32.dll")]
        static extern int GetRgnBox(IntPtr region, out RECT box);

        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr handle);

        [DllImport("dwmapi.dll")]
        static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    }
}
'@
}

$null = [CursorMeterProbe.Probe]::MakeDpiAware()
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$VkWin = [byte]0x5B
$VkTab = [byte]0x09
$VkEsc = [byte]0x1B
$LogPath = Join-Path $env:LOCALAPPDATA 'CursorMeter\cursormeter.log'

# ---------------------------------------------------------------- helpers

function Get-Reading { return [CursorMeterProbe.Probe]::Measure() }

function Get-RectText($l, $t, $r, $b) { return ('{0},{1} {2}x{3}' -f $l, $t, ($r - $l), ($b - $t)) }

function Get-WidgetText($x) { return (Get-RectText $x.WidgetL $x.WidgetT $x.WidgetR $x.WidgetB) }

function Get-RegionName($x) {
    switch ($x.RegionKind) {
        0 { return 'nenhuma' }
        1 { return 'vazia' }
        2 { return ('simples caixa=' + (Get-RectText $x.RegionL $x.RegionT $x.RegionR $x.RegionB)) }
        default { return ('complexa caixa=' + (Get-RectText $x.RegionL $x.RegionT $x.RegionR $x.RegionB)) }
    }
}

function Test-SameWidget($a, $b) {
    return ($a.WidgetL -eq $b.WidgetL -and $a.WidgetT -eq $b.WidgetT -and $a.WidgetR -eq $b.WidgetR -and $a.WidgetB -eq $b.WidgetB)
}

function Test-SameRegion($a, $b) {
    return ($a.RegionKind -eq $b.RegionKind -and $a.RegionL -eq $b.RegionL -and $a.RegionT -eq $b.RegionT -and $a.RegionR -eq $b.RegionR -and $a.RegionB -eq $b.RegionB)
}

function Test-OverlapsTaskbar($x) {
    return ($x.WidgetL -lt $x.TaskbarR -and $x.WidgetR -gt $x.TaskbarL -and $x.WidgetT -lt $x.TaskbarB -and $x.WidgetB -gt $x.TaskbarT)
}

# Start11 cuts the taskbar into islands with a window region (box in taskbar coordinates). Bars in the plain
# overlay mode that sit over that box cover taskbar icons: the island of their own did not fit on the left.
function Get-OverlapWarning($x, [string]$mode) {
    if ($mode -ne 'normal' -or -not $x.WidgetFound -or $x.RegionKind -ne 3) { return '' }
    $left = $x.TaskbarL + $x.RegionL
    $right = $x.TaskbarL + $x.RegionR
    $top = $x.TaskbarT + $x.RegionT
    $bottom = $x.TaskbarT + $x.RegionB
    if ($x.WidgetL -lt $right -and $x.WidgetR -gt $left -and $x.WidgetT -lt $bottom -and $x.WidgetB -gt $top) {
        return ('as barras ({0}) estao por cima da regiao do Start11 (x {1} a {2}) e cobrem icones da barra. Provavel falta de espaco a esquerda para a ilha das barras (muitas janelas abertas).' -f (Get-WidgetText $x), $left, $right)
    }
    return ''
}

function Get-PlacementLine {
    if (-not (Test-Path -LiteralPath $LogPath)) { return '' }
    try {
        $line = Get-Content -LiteralPath $LogPath -Tail 300 | Where-Object { $_ -match 'Placement:' } | Select-Object -Last 1
    } catch {
        return ''
    }
    if ($line) { return [string]$line }
    return ''
}

# Names the placement mode from the app log. "ilha" is Start11, "corte" is the 1.2.6 cut, "ao-lado" is 1.2.5 or the fallback.
function Get-ModeName([string]$line) {
    if ([string]::IsNullOrEmpty($line)) { return 'sem-log' }
    if ($line -match 'Placement: island') { return 'ilha' }
    if ($line -match 'Placement: floating') { return 'flutuante' }
    if ($line -match 'overlay through the taskbar') { return 'corte' }
    if ($line -match 'overlay beside the taskbar') { return 'ao-lado' }
    if ($line -match 'overlay on the taskbar') { return 'normal' }
    if ($line -match 'embedded in the taskbar') { return 'embutido' }
    return 'outro'
}

function Wait-Until([scriptblock]$condition, [int]$milliseconds = 3000) {
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    while ($watch.ElapsedMilliseconds -lt $milliseconds) {
        if (& $condition) { return $true }
        Start-Sleep -Milliseconds 100
    }
    return $false
}

# Whole primary screen, so a person can see what covers the bars.
function Save-Screen([string]$path) {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = New-Object System.Drawing.Bitmap -ArgumentList $bounds.Width, $bounds.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($bounds.Left, $bounds.Top, 0, 0, $bounds.Size)
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Save-Capture($x, [string]$path) {
    $width = $x.WidgetR - $x.WidgetL
    $height = $x.WidgetB - $x.WidgetT
    if ($width -le 0 -or $height -le 0) { return $null }
    $bitmap = New-Object System.Drawing.Bitmap -ArgumentList $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($x.WidgetL, $x.WidgetT, 0, 0, (New-Object System.Drawing.Size -ArgumentList $width, $height))
    } finally {
        $graphics.Dispose()
    }
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    return $bitmap
}

# Fraction of pixels that match the reference within a small tolerance. -1 when the sizes differ.
function Compare-Capture($reference, $other) {
    if ($null -eq $reference -or $null -eq $other) { return -1.0 }
    if ($reference.Width -ne $other.Width -or $reference.Height -ne $other.Height) { return -1.0 }
    $same = 0
    for ($y = 0; $y -lt $reference.Height; $y++) {
        for ($x = 0; $x -lt $reference.Width; $x++) {
            $a = $reference.GetPixel($x, $y)
            $b = $other.GetPixel($x, $y)
            $d = [Math]::Max([Math]::Abs([int]$a.R - [int]$b.R), [Math]::Max([Math]::Abs([int]$a.G - [int]$b.G), [Math]::Abs([int]$a.B - [int]$b.B)))
            if ($d -le 24) { $same++ }
        }
    }
    return [double]$same / ($reference.Width * $reference.Height)
}

$script:Results = New-Object System.Collections.Generic.List[object]

function Add-Result([string]$case, [string]$test, [string]$state, [string]$detail) {
    $script:Results.Add([pscustomobject]@{ Caso = $case; Teste = $test; Estado = $state; Detalhe = $detail })
    $color = 'Gray'
    if ($state -eq 'PASSA') { $color = 'Green' }
    elseif ($state -eq 'FALHA') { $color = 'Red' }
    elseif ($state -eq 'AVISO') { $color = 'Yellow' }
    elseif ($state -eq 'INCONCLUSIVO') { $color = 'DarkYellow' }
    Write-Host ('  [{0}] {1}: {2}' -f $state, $test, $detail) -ForegroundColor $color
}

function Get-Start11 {
    $names = Get-Process | Where-Object { $_.ProcessName -like 'Start11*' } | ForEach-Object { $_.ProcessName } | Sort-Object -Unique
    return @($names)
}

function Get-MeterInfo {
    $process = Get-Process | Where-Object { $_.ProcessName -like 'CursorMeter*' } | Select-Object -First 1
    if (-not $process) { return [pscustomobject]@{ Running = $false; Path = ''; Version = ''; Name = '' } }
    $version = ''
    $path = ''
    try {
        $path = $process.Path
        $version = (Get-Item -LiteralPath $path).VersionInfo.ProductVersion
    } catch {
    }
    return [pscustomobject]@{ Running = $true; Path = $path; Version = $version; Name = $process.ProcessName }
}

function Get-Environment {
    $os = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    # The registry still says "Windows 10" on Windows 11. From build 22000 it is Windows 11.
    $productName = [string]$os.ProductName
    if ([int]$os.CurrentBuild -ge 22000) { $productName = $productName -replace 'Windows 10', 'Windows 11' }
    $x = Get-Reading
    $meter = Get-MeterInfo
    $start11 = Get-Start11
    $edge = 'desconhecida'
    if ($x.TaskbarFound) {
        $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        $horizontal = ($x.TaskbarR - $x.TaskbarL) -ge ($x.TaskbarB - $x.TaskbarT)
        if ($horizontal) {
            if ($x.TaskbarT -le ($screen.Top + $screen.Height / 3)) { $edge = 'cima' } else { $edge = 'baixo' }
        } else {
            if ($x.TaskbarL -le ($screen.Left + $screen.Width / 3)) { $edge = 'esquerda' } else { $edge = 'direita' }
        }
    }
    return [pscustomobject]@{
        Windows   = ('{0} (compilacao {1}.{2})' -f $productName, $os.CurrentBuild, $os.UBR)
        Escala    = ('{0}% (DPI {1})' -f [Math]::Round($x.Dpi * 100 / 96), $x.Dpi)
        Monitores = [System.Windows.Forms.Screen]::AllScreens.Count
        Barra     = if ($x.TaskbarFound) { ('{0}, lado {1}, camada {2}' -f (Get-RectText $x.TaskbarL $x.TaskbarT $x.TaskbarR $x.TaskbarB), $edge, [CursorMeterProbe.Probe]::BandName([uint32]([Math]::Max($x.TaskbarBand, 0)))) } else { 'sem Shell_TrayWnd' }
        Regiao    = Get-RegionName $x
        Start11   = if ($start11.Count -gt 0) { ($start11 -join ', ') } else { 'nao' }
        App       = if ($meter.Running) { ('{0} {1}' -f $meter.Name, $meter.Version) } else { 'nao esta a correr' }
        Barras    = if ($x.WidgetFound) { Get-WidgetText $x } else { 'sem janela' }
        Modo      = (Get-ModeName (Get-PlacementLine))
        Linha     = (Get-PlacementLine)
        Raw       = $x
    }
}

function Write-Environment($e) {
    Write-Host ''
    Write-Host 'Ambiente' -ForegroundColor Cyan
    Write-Host ('  Windows    : {0}' -f $e.Windows)
    Write-Host ('  Escala     : {0}    Monitores: {1}' -f $e.Escala, $e.Monitores)
    Write-Host ('  Barra      : {0}' -f $e.Barra)
    Write-Host ('  Regiao     : {0}' -f $e.Regiao)
    Write-Host ('  Start11    : {0}' -f $e.Start11)
    Write-Host ('  CursorMeter: {0}' -f $e.App)
    Write-Host ('  Barras     : {0}' -f $e.Barras)
    Write-Host ('  Modo (log) : {0}  {1}' -f $e.Modo, $e.Linha)
    $overlap = Get-OverlapWarning $e.Raw $e.Modo
    if ($overlap) { Write-Host ('  AVISO      : ' + $overlap) -ForegroundColor Yellow }
}

function Send-Open([string]$kind) {
    if ($kind -eq 'vista') { [CursorMeterProbe.Probe]::KeyChord($VkWin, $VkTab) } else { [CursorMeterProbe.Probe]::KeyTap($VkWin) }
}

function Send-Close([string]$kind) {
    if ($kind -eq 'vista') { [CursorMeterProbe.Probe]::KeyTap($VkEsc) } else { [CursorMeterProbe.Probe]::KeyTap($VkWin) }
}

# ---------------------------------------------------------------- one open/close cycle

function Invoke-Case([string]$name, [string]$kind) {
    Write-Host ''
    Write-Host ('Caso: {0}' -f $name) -ForegroundColor Cyan

    $base = Get-Reading
    if (-not $base.TaskbarFound) { Add-Result $name 'arranque' 'FALHA' 'nao ha Shell_TrayWnd'; return }
    if (-not $base.WidgetFound) { Add-Result $name 'arranque' 'FALHA' 'o CursorMeter nao esta a correr ou nao tem janela de barras'; return }
    if ($base.TaskbarBand -ne 1 -or $base.StartOpen) {
        Add-Result $name 'arranque' 'INCONCLUSIVO' 'o menu ou a Vista de Tarefas ja estavam abertos, ou a barra nao esta na camada DESKTOP. Fecha-os e repete.'
        return
    }

    $baseMode = Get-ModeName (Get-PlacementLine)
    $start11 = Get-Start11
    $expectCut = ($baseMode -eq 'normal') -and ($base.RegionKind -eq 0 -or $base.RegionKind -eq 2)
    $baseBitmap = Save-Capture $base (Join-Path $Saida ('{0}-1-fechado-antes.png' -f $name))
    Add-Result $name 'antes' 'INFO' ('barras {0}, modo {1}, regiao {2}, Start11 {3}' -f (Get-WidgetText $base), $baseMode, (Get-RegionName $base), $(if ($start11.Count -gt 0) { 'sim' } else { 'nao' }))
    $overlapNote = Get-OverlapWarning $base $baseMode
    if ($overlapNote) { Add-Result $name 'sobreposicao' 'AVISO' $overlapNote }
    if ($baseMode -eq 'flutuante') {
        if (-not (Test-OverlapsTaskbar $base)) { Add-Result $name 'flutuante' 'PASSA' ('barras a flutuar fora da barra de tarefas ({0}), sem tapar icones' -f (Get-WidgetText $base)) }
        else { Add-Result $name 'flutuante' 'AVISO' ('as barras flutuantes estao sobre a barra de tarefas ({0}): foram arrastadas para la, ou ha um erro' -f (Get-WidgetText $base)) }
    }
    if ($baseMode -eq 'embutido') {
        Add-Result $name 'arranque' 'INCONCLUSIVO' 'as barras estao embutidas na barra (Windows 10). Este teste e para o Windows 11.'
        return
    }

    $samples = @()
    $opened = $false
    $noRaise = $false
    try {
        Send-Open $kind
        $opened = Wait-Until { $r = Get-Reading; ($r.TaskbarBand -ne $base.TaskbarBand) -or $r.StartOpen } 3000
        if (-not $opened) {
            # The taskbar stayed in its band (Start11 draws its own menu). The recut and the mode cannot be
            # judged, but whether the bars are still visible can: measure it and keep the whole screen.
            $noRaise = $true
            $opened = $true
            Start-Sleep -Milliseconds 1200
            $s = Get-Reading
            $bitmap = Save-Capture $s (Join-Path $Saida ('{0}-2-aberto-sem-camada.png' -f $name))
            Save-Screen (Join-Path $Saida ('{0}-2-aberto-sem-camada-ecra.png' -f $name))
            $sim = Compare-Capture $baseBitmap $bitmap
            Add-Result $name 'abrir' 'INCONCLUSIVO' 'a barra nao mudou de camada (comum com Start11): nao da para julgar o recorte nem o modo. Confirma a captura do ecra para ver se o menu abriu.'
            if ($sim -lt 0) { Add-Result $name 'visivel' 'INCONCLUSIVO' 'o tamanho da janela mudou, nao da para comparar as capturas' }
            elseif ($kind -eq 'vista') { Add-Result $name 'visivel' 'INFO' ('durante a Vista de Tarefas e aceitavel que fiquem tapadas: {0:P0} dos pixeis iguais ao antes' -f $sim) }
            elseif ($sim -ge $LimiarPixeis) { Add-Result $name 'visivel' 'PASSA' ('{0:P0} dos pixeis iguais ao antes, mesmo sem a barra mudar de camada' -f $sim) }
            else { Add-Result $name 'visivel' 'FALHA' ('as barras estao tapadas ou mudaram: {0:P0} dos pixeis iguais ao antes (limiar {1:P0}). Ver {2}' -f $sim, $LimiarPixeis, $Saida) }
            if (-not (Test-SameWidget $base $s)) { Add-Result $name 'posicao' 'FALHA' ('as barras mexeram-se: {0} -> {1}' -f (Get-WidgetText $base), (Get-WidgetText $s)) }
            return
        }

        $previous = 0
        foreach ($wait in @(300, 1000, 2000)) {
            Start-Sleep -Milliseconds ($wait - $previous)
            $previous = $wait
            $s = Get-Reading
            $bitmap = Save-Capture $s (Join-Path $Saida ('{0}-2-aberto-{1}ms.png' -f $name, $wait))
            Save-Screen (Join-Path $Saida ('{0}-2-aberto-{1}ms-ecra.png' -f $name, $wait))
            $samples += [pscustomobject]@{ Wait = $wait; Reading = $s; Bitmap = $bitmap; Mode = (Get-ModeName (Get-PlacementLine)) }
            Write-Host ('    +{0,4} ms  camada={1}  menu={2}  barras={3}  regiao={4}' -f $wait, [CursorMeterProbe.Probe]::BandName([uint32]([Math]::Max($s.TaskbarBand, 0))), $s.StartOpen, (Get-WidgetText $s), (Get-RegionName $s))
        }

        $settled = @($samples | Where-Object { $_.Wait -ge 1000 })
        $stillRaised = @($settled | Where-Object { $_.Reading.TaskbarBand -ne $base.TaskbarBand }).Count -eq $settled.Count
        if (-not $stillRaised) {
            Add-Result $name 'abrir' 'INCONCLUSIVO' 'o menu fechou sozinho antes das medidas de 1 s e 2 s.'
            return
        }

        $last = $samples[-1]
        $openMode = $last.Mode
        $moved = -not (Test-SameWidget $base $last.Reading)
        $outside = -not (Test-OverlapsTaskbar $last.Reading)

        # 1. Mode and position
        if ($openMode -eq 'corte') {
            $cutOk = @($settled | Where-Object { $_.Reading.RegionKind -eq 3 }).Count -eq $settled.Count
            if ($cutOk -and -not $moved) { Add-Result $name 'recorte' 'PASSA' ('modo corte, regiao complexa na barra ({0}), barras no mesmo sitio' -f (Get-RegionName $last.Reading)) }
            elseif (-not $cutOk) { Add-Result $name 'recorte' 'FALHA' ('o log diz corte mas a barra tem regiao {0}' -f (Get-RegionName $last.Reading)) }
            else { Add-Result $name 'recorte' 'FALHA' ('modo corte mas as barras mexeram-se: {0} -> {1}' -f (Get-WidgetText $base), (Get-WidgetText $last.Reading)) }
        }
        elseif ($openMode -eq 'ilha') {
            if (-not $moved) { Add-Result $name 'ilha' 'PASSA' 'modo ilha (Start11), barras no mesmo sitio e sem recorte' }
            else { Add-Result $name 'ilha' 'FALHA' ('modo ilha mas as barras mexeram-se: {0} -> {1}' -f (Get-WidgetText $base), (Get-WidgetText $last.Reading)) }
        }
        elseif ($openMode -eq 'flutuante') {
            if (-not $moved) { Add-Result $name 'flutuante' 'PASSA' 'modo flutuante, barras no mesmo sitio e sem recorte' }
            else { Add-Result $name 'flutuante' 'FALHA' ('modo flutuante mas as barras mexeram-se: {0} -> {1}' -f (Get-WidgetText $base), (Get-WidgetText $last.Reading)) }
        }
        elseif ($openMode -eq 'ao-lado') {
            if ($expectCut) {
                Add-Result $name 'recorte' 'FALHA' ('as barras sairam da barra ({0} -> {1}) em vez de ficarem recortadas. E o comportamento da 1.2.5, ou o recorte foi recusado.' -f (Get-WidgetText $base), (Get-WidgetText $last.Reading))
            }
            elseif ($outside) {
                Add-Result $name 'recurso' 'AVISO' ('modo ao-lado: as barras saem da barra enquanto o menu esta aberto ({0}). Esperado com regiao complexa ou Start11, mas as barras nao ficam na barra.' -f (Get-WidgetText $last.Reading))
            }
            else {
                Add-Result $name 'recurso' 'FALHA' 'o log diz ao-lado mas a janela continua sobre a barra'
            }
        }
        elseif ($openMode -eq 'normal') {
            Add-Result $name 'modo' 'FALHA' 'a barra mudou de camada mas a app continua em modo normal: nao reagiu ao menu'
        }
        else {
            Add-Result $name 'modo' 'INCONCLUSIVO' ('modo {0}: {1}' -f $openMode, (Get-PlacementLine))
        }

        # 2. Visible pixels
        $sims = @()
        foreach ($s in $settled) {
            $sims += (Compare-Capture $baseBitmap $s.Bitmap)
        }
        if (@($sims | Where-Object { $_ -lt 0 }).Count -gt 0) {
            Add-Result $name 'pixeis' 'INCONCLUSIVO' 'o tamanho da janela mudou, nao da para comparar as capturas'
        }
        else {
            $min = ($sims | Measure-Object -Minimum).Minimum
            $text = ('{0:P0} dos pixeis iguais ao antes (limiar {1:P0}), capturas em {2}' -f $min, $LimiarPixeis, $Saida)
            if ($kind -eq 'vista') {
                # Task View covers the whole screen, so the bars being hidden while it is open is accepted.
                # What counts is that they come back, which is checked after closing.
                Add-Result $name 'visivel' 'INFO' ('durante a Vista de Tarefas e aceitavel que fiquem tapadas: ' + $text)
            }
            elseif ($min -ge $LimiarPixeis) { Add-Result $name 'visivel' 'PASSA' $text }
            else { Add-Result $name 'visivel' 'FALHA' ('as barras nao estao visiveis, ou estao tapadas: ' + $text) }
        }

        # 3. Hardware sanity: the window width should follow the scale
        $expectedWidth = [Math]::Round(252 * $base.Dpi / 96)
        $width = $base.WidgetR - $base.WidgetL
        if ($baseMode -eq 'normal' -and [Math]::Abs($width - $expectedWidth) -gt 2) {
            Add-Result $name 'escala' 'AVISO' ('largura das barras {0} px, esperado cerca de {1} px: o script pode nao estar com DPI real' -f $width, $expectedWidth)
        }
    }
    finally {
        if ($opened -and $noRaise) {
            # Esc closes whatever opened and does nothing if nothing did. The Windows key would toggle it back open.
            [CursorMeterProbe.Probe]::KeyTap($VkEsc)
            Start-Sleep -Milliseconds 800
        }
        elseif ($opened) {
            Send-Close $kind
            $closed = Wait-Until { $r = Get-Reading; ($r.TaskbarBand -eq $base.TaskbarBand) -and -not $r.StartOpen } 3000
            if (-not $closed) {
                [CursorMeterProbe.Probe]::KeyTap($VkEsc)
                $null = Wait-Until { $r = Get-Reading; ($r.TaskbarBand -eq $base.TaskbarBand) -and -not $r.StartOpen } 3000
            }
        }
    }

    if (-not $opened) { return }

    # 4. After closing: everything back to how it was
    Start-Sleep -Milliseconds 800
    $after = Get-Reading
    $afterBitmap = Save-Capture $after (Join-Path $Saida ('{0}-3-fechado-depois.png' -f $name))
    $afterMode = Get-ModeName (Get-PlacementLine)
    if ($after.TaskbarBand -ne $base.TaskbarBand -or $after.StartOpen) {
        Add-Result $name 'fechar' 'INCONCLUSIVO' 'o menu nao fechou a tempo'
        return
    }
    if (Test-SameRegion $base $after) { Add-Result $name 'regiao' 'PASSA' ('regiao da barra igual ao antes ({0})' -f (Get-RegionName $after)) }
    else { Add-Result $name 'regiao' 'FALHA' ('a regiao da barra nao voltou ao que era: antes {0}, depois {1}' -f (Get-RegionName $base), (Get-RegionName $after)) }
    if (Test-SameWidget $base $after) { Add-Result $name 'posicao' 'PASSA' ('barras de volta a {0}' -f (Get-WidgetText $after)) }
    else { Add-Result $name 'posicao' 'FALHA' ('as barras nao voltaram: antes {0}, depois {1}' -f (Get-WidgetText $base), (Get-WidgetText $after)) }
    if ($afterMode -eq $baseMode) { Add-Result $name 'modo' 'PASSA' ('modo de volta a {0}' -f $afterMode) }
    else { Add-Result $name 'modo' 'FALHA' ('o modo nao voltou: antes {0}, depois {1}' -f $baseMode, $afterMode) }
    $simAfter = Compare-Capture $baseBitmap $afterBitmap
    if ($simAfter -lt 0) { Add-Result $name 'visivel-depois' 'INCONCLUSIVO' 'o tamanho da janela mudou, nao da para comparar as capturas' }
    elseif ($simAfter -ge $LimiarPixeis) { Add-Result $name 'visivel-depois' 'PASSA' ('barras visiveis outra vez: {0:P0} dos pixeis iguais ao antes' -f $simAfter) }
    else { Add-Result $name 'visivel-depois' 'FALHA' ('as barras nao voltaram a aparecer: {0:P0} dos pixeis iguais ao antes (limiar {1:P0})' -f $simAfter, $LimiarPixeis) }
}

# ---------------------------------------------------------------- crash with the menu open

function Invoke-CrashTest {
    $name = 'crash'
    Write-Host ''
    Write-Host 'Caso: crash com o menu aberto' -ForegroundColor Cyan
    $base = Get-Reading
    $meter = Get-MeterInfo
    $baseMode = Get-ModeName (Get-PlacementLine)
    $start11 = Get-Start11
    if (-not $base.WidgetFound -or -not $meter.Running) { Add-Result $name 'arranque' 'FALHA' 'o CursorMeter nao esta a correr'; return }
    if (-not ($baseMode -eq 'normal' -and ($base.RegionKind -eq 0 -or $base.RegionKind -eq 2))) {
        Add-Result $name 'arranque' 'INCONCLUSIVO' ('so tem sentido quando as barras recortam a barra (modo normal, sem regiao). Modo {0}, regiao {1}.' -f $baseMode, (Get-RegionName $base))
        return
    }
    if ($base.TaskbarBand -ne 1 -or $base.StartOpen) { Add-Result $name 'arranque' 'INCONCLUSIVO' 'fecha o menu e repete'; return }
    if (-not $Sim) {
        $answer = Read-Host 'Vou terminar o CursorMeter com o menu aberto e voltar a abri-lo. Continuar? (s/N)'
        if ($answer -notmatch '^[sSyY]') { Add-Result $name 'arranque' 'INCONCLUSIVO' 'cancelado'; return }
    }

    $exe = $meter.Path
    try {
        Send-Open 'iniciar'
        $cut = Wait-Until { $r = Get-Reading; $r.TaskbarBand -ne $base.TaskbarBand -and $r.RegionKind -eq 3 } 4000
        if (-not $cut) { Add-Result $name 'preparar' 'INCONCLUSIVO' 'o recorte nao foi aplicado com o menu aberto, nao ha o que testar'; return }

        Get-Process | Where-Object { $_.ProcessName -like 'CursorMeter*' } | Stop-Process -Force
        Start-Sleep -Milliseconds 800
        Send-Close 'iniciar'
        $null = Wait-Until { $r = Get-Reading; $r.TaskbarBand -eq $base.TaskbarBand -and -not $r.StartOpen } 3000
        Start-Sleep -Milliseconds 500

        $left = Get-Reading
        if ($left.RegionKind -eq 3) { Add-Result $name 'sem-app' 'FALHA' ('o recorte ficou na barra com a app morta e o menu fechado: {0}. Ve-se como um buraco na barra.' -f (Get-RegionName $left)) }
        else { Add-Result $name 'sem-app' 'PASSA' 'a barra voltou ao normal mesmo sem a app' }

        if ($exe) {
            Start-Process -FilePath $exe | Out-Null
            $back = Wait-Until { $r = Get-Reading; $r.WidgetFound } 15000
            Start-Sleep -Milliseconds 2500
            $restarted = Get-Reading
            if (-not $back) { Add-Result $name 'rearranque' 'INCONCLUSIVO' 'a app nao voltou a abrir a tempo' }
            elseif ($left.RegionKind -eq 3 -and $restarted.RegionKind -eq 3) { Add-Result $name 'rearranque' 'FALHA' 'a app voltou a abrir e nao limpou o recorte antigo da barra' }
            elseif ($left.RegionKind -eq 3) { Add-Result $name 'rearranque' 'PASSA' 'a app limpou o recorte antigo ao arrancar' }
        }
        else {
            Add-Result $name 'rearranque' 'INCONCLUSIVO' 'nao consegui o caminho do executavel para o reabrir'
        }
    }
    finally {
        $now = Get-Reading
        if ($now.StartOpen -or $now.TaskbarBand -ne $base.TaskbarBand) { Send-Close 'iniciar'; Start-Sleep -Milliseconds 600 }
        $now = Get-Reading
        if ($base.RegionKind -ne 3 -and $now.RegionKind -eq 3) {
            $null = [CursorMeterProbe.Probe]::ClearTaskbarRegion()
            Write-Host '  (limpei o recorte que ficou na barra)' -ForegroundColor DarkGray
        }
        if (-not (Get-MeterInfo).Running -and $exe) { Start-Process -FilePath $exe | Out-Null }
    }
}

# ---------------------------------------------------------------- watch mode

function Invoke-Observar {
    Write-Host ('A observar {0} s. Abre o menu Iniciar, espera um segundo e fecha-o.' -f $Segundos)
    $deadline = (Get-Date).AddSeconds($Segundos)
    $previous = ''
    while ((Get-Date) -lt $deadline) {
        try {
            $sample = [CursorMeterProbe.Probe]::Sample()
        } catch {
            Write-Host $_.Exception.ToString()
            break
        }
        if ($sample -ne $previous) {
            Write-Host ('{0:HH:mm:ss.fff}  {1}' -f (Get-Date), $sample)
            $previous = $sample
        }
        Start-Sleep -Milliseconds 200
    }
    Write-Host 'fim'
}

# ---------------------------------------------------------------- main

$script:ExitCode = 0

# Copy of the app's own log, so the run and the app's view of it sit in the same folder.
function Save-AppLog {
    try {
        if (Test-Path -LiteralPath $LogPath) {
            Get-Content -LiteralPath $LogPath -Tail 500 | Set-Content -LiteralPath (Join-Path $Saida 'cursormeter.log') -Encoding UTF8
        }
    } catch {
        Write-Host ('(nao consegui copiar o registo da app: {0})' -f $_.Exception.Message) -ForegroundColor DarkYellow
    }
}

$script:ExtraWindows = @()

# Opens Notepad windows to fill the taskbar. Only the ones started here are closed afterwards.
function Open-ExtraWindows([int]$count) {
    $before = @(Get-Process -Name Notepad -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
    Write-Host ('A abrir {0} janelas do Bloco de Notas para encher a barra de tarefas...' -f $count)
    for ($i = 0; $i -lt $count; $i++) {
        Start-Process -FilePath 'notepad.exe'
        Start-Sleep -Milliseconds 700
    }
    Start-Sleep -Seconds 3
    $script:ExtraWindows = @(Get-Process -Name Notepad -ErrorAction SilentlyContinue | Where-Object { $before -notcontains $_.Id })
    Write-Host ('Janelas abertas pelo script: {0}' -f $script:ExtraWindows.Count)
}

function Close-ExtraWindows {
    if ($script:ExtraWindows.Count -eq 0) { return }
    Write-Host ('A fechar as {0} janelas do Bloco de Notas abertas pelo script.' -f $script:ExtraWindows.Count)
    foreach ($process in $script:ExtraWindows) {
        try { if (-not $process.HasExited) { [void]$process.CloseMainWindow() } } catch { }
    }
    Start-Sleep -Seconds 2
    foreach ($process in $script:ExtraWindows) {
        try { if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force } } catch { }
    }
    $script:ExtraWindows = @()
}

function Invoke-Main {
    # The folder comes first so that the log covers the whole run, including the environment.
    $root = Join-Path $env:LOCALAPPDATA 'CursorMeter\testes'
    if ([string]::IsNullOrEmpty($Saida)) {
        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $folder = if ($Etiqueta) { '{0}-{1}' -f $stamp, $Etiqueta } else { $stamp }
        # Script scope: Invoke-Case and the log copy read it too.
        $script:Saida = Join-Path $root $folder
    }
    New-Item -ItemType Directory -Force -Path $Saida | Out-Null
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    Set-Content -LiteralPath (Join-Path $root 'ultimo.txt') -Value $Saida -Encoding UTF8
    Start-Transcript -Path (Join-Path $Saida 'teste.log') -Force | Out-Null

    $commit = ''
    # Only inside a clone: from the unzipped copy git would print an error into the log.
    if (Test-Path -LiteralPath (Join-Path $PSScriptRoot '..\.git')) {
        try { $commit = (& git -C $PSScriptRoot rev-parse --short HEAD 2>$null) } catch { }
    }
    Write-Host ('Teste do menu Iniciar do CursorMeter, {0:yyyy-MM-dd HH:mm:ss}' -f (Get-Date))
    Write-Host ('Script: {0}  commit: {1}' -f $PSCommandPath, $(if ($commit) { $commit } else { '?' }))
    Write-Host ('Argumentos: {0}' -f $(if ($script:BoundArgs.Count) { (($script:BoundArgs.GetEnumerator() | ForEach-Object { if ($_.Value -is [System.Management.Automation.SwitchParameter]) { '-' + $_.Key } else { '-{0} {1}' -f $_.Key, $_.Value } }) -join ' ') } else { '(nenhum)' }))
    Write-Host ('PowerShell {0}, utilizador {1}, maquina {2}' -f $PSVersionTable.PSVersion, $env:USERNAME, $env:COMPUTERNAME)
    Write-Host ('Pasta do teste e do log: {0}' -f $Saida)

    if ($Observar) { Invoke-Observar; Save-AppLog; return }

    if ($AbrirJanelas -gt 0) { Open-ExtraWindows $AbrirJanelas }
    $environment = Get-Environment
    Write-Environment $environment
    if ($SoAmbiente) { Save-AppLog; return }

    Write-Host ''
Write-Host 'Nao uses o teclado nem o rato durante o teste. A comecar em 3 s...' -ForegroundColor Yellow
Start-Sleep -Seconds 3

Invoke-Case 'iniciar' 'iniciar'
if ($VistaDeTarefas) { Invoke-Case 'vista-de-tarefas' 'vista' }
if ($TesteCrash) { Invoke-CrashTest }

$failures = @($script:Results | Where-Object { $_.Estado -eq 'FALHA' }).Count
$warnings = @($script:Results | Where-Object { $_.Estado -eq 'AVISO' }).Count
$inconclusive = @($script:Results | Where-Object { $_.Estado -eq 'INCONCLUSIVO' }).Count

$markdown = New-Object System.Text.StringBuilder
[void]$markdown.AppendLine('# Teste do menu Iniciar')
[void]$markdown.AppendLine('')
[void]$markdown.AppendLine(('Data: {0:yyyy-MM-dd HH:mm:ss}  Etiqueta: {1}' -f (Get-Date), $Etiqueta))
[void]$markdown.AppendLine('')
foreach ($line in @(
    ('- Windows: {0}' -f $environment.Windows),
    ('- Escala: {0}, monitores: {1}' -f $environment.Escala, $environment.Monitores),
    ('- Barra: {0}' -f $environment.Barra),
    ('- Start11: {0}' -f $environment.Start11),
    ('- CursorMeter: {0}' -f $environment.App),
    ('- Modo no arranque: {0} ({1})' -f $environment.Modo, $environment.Linha))) { [void]$markdown.AppendLine($line) }
[void]$markdown.AppendLine('')
[void]$markdown.AppendLine('| Caso | Teste | Estado | Detalhe |')
[void]$markdown.AppendLine('| --- | --- | --- | --- |')
foreach ($row in $script:Results) {
    [void]$markdown.AppendLine(('| {0} | {1} | {2} | {3} |' -f $row.Caso, $row.Teste, $row.Estado, ($row.Detalhe -replace '\|', '/')))
}
[void]$markdown.AppendLine('')
[void]$markdown.AppendLine(('Falhas: {0}, avisos: {1}, inconclusivos: {2}' -f $failures, $warnings, $inconclusive))
Set-Content -LiteralPath (Join-Path $Saida 'resultado.md') -Value $markdown.ToString() -Encoding UTF8

$report = [pscustomobject]@{
    Data = (Get-Date).ToString('o')
    Etiqueta = $Etiqueta
    Ambiente = ($environment | Select-Object Windows, Escala, Monitores, Barra, Regiao, Start11, App, Barras, Modo, Linha)
    Resultados = $script:Results
}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Saida 'resultado.json') -Encoding UTF8

Write-Host ''
if ($failures -gt 0) { Write-Host ('{0} falha(s), {1} aviso(s), {2} inconclusivo(s).' -f $failures, $warnings, $inconclusive) -ForegroundColor Red }
else { Write-Host ('Sem falhas. {0} aviso(s), {1} inconclusivo(s).' -f $warnings, $inconclusive) -ForegroundColor Green }
Save-AppLog
Write-Host ('Resultados e capturas em: {0}' -f $Saida)
Write-Host ('Log completo do teste:    {0}' -f (Join-Path $Saida 'teste.log'))
Write-Host ('Registo da app (copia):   {0}' -f (Join-Path $Saida 'cursormeter.log'))
Write-Host 'Repete a 125% e 150% de escala e, se puderes, num PC com e sem Start11 (-Etiqueta separa as pastas).'
if ($failures -gt 0) { $script:ExitCode = 1 }
}

try {
    Invoke-Main | Out-Null
} catch {
    Write-Host ('ERRO: ' + $_.Exception.ToString()) -ForegroundColor Red
    $script:ExitCode = 2
} finally {
    try { Close-ExtraWindows } catch { }
    try { Stop-Transcript | Out-Null } catch { }
}
exit $script:ExitCode
