@echo off
rem Corre o teste do menu Iniciar (Iniciar + Vista de Tarefas) e guarda o log.
rem Duplo clique, ou na consola: scripts\test-start-menu.cmd [-Etiqueta 125pct] [-TesteCrash] [-SoAmbiente]
rem Auto-ocultacao da barra de tarefas: acrescenta -AutoOcultacao (ou -SoAutoOcultacao para so esse caso).
rem Nao uses o teclado nem o rato enquanto corre. Defina NOPAUSE=1 para nao esperar no fim.
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0test-start-menu.ps1" -VistaDeTarefas %*
set "CODE=%ERRORLEVEL%"
echo.
if exist "%LOCALAPPDATA%\CursorMeter\testes\ultimo.txt" (
  set /p LAST=<"%LOCALAPPDATA%\CursorMeter\testes\ultimo.txt"
  setlocal enabledelayedexpansion
  echo Log em: !LAST!\teste.log
  endlocal
)
if not defined NOPAUSE pause
exit /b %CODE%
