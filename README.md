# CursorMeter

Duas barras na barra de tarefas do Windows, só para o Cursor:

`37% Cursor Models | 64% Other Models`

Cada barra é a quota desse pool no ciclo atual. Cursor Models corresponde a `autoPercentUsed`. Other Models corresponde a `apiPercentUsed`. São as mesmas duas percentagens do painel Usage.

Lê a sessão que o próprio Cursor gravou ao iniciares sessão, em `%APPDATA%\Cursor\User\globalStorage\state.vscdb`. Se essa sessão expirar, usa a sessão de cursor.com que estiver no Firefox.

## Instalar

A release traz um instalador para Windows de 64 bits. Não pede administrador: o programa fica em `%LOCALAPPDATA%\Programs\CursorMeter` e aparece em Aplicações instaladas. A posição das barras continua em `%LOCALAPPDATA%\CursorMeter\offset.txt`, por isso uma reinstalação não a apaga.

O Windows pode dizer que o publicador é desconhecido, porque o instalador não está assinado. No assistente podes deixar marcado o arranque com o Windows. O mesmo interruptor está no menu do botão direito.

## Correr a partir do código

No Windows, com o [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) e o [Inno Setup 6](https://jrsoftware.org/isdl.php) se fores gerar o instalador:

```powershell
.\scripts\build-windows.ps1
.\src\CursorMeter.App\bin\Release\net8.0-windows\win-x64\publish\CursorMeter.exe
```

O instalador, `dist\CursorMeter-Setup-<versão>.exe`, sai de `.\scripts\build-installer.ps1`.

O executável fica ao lado do relógio, dentro da barra de tarefas. Arrasta para o reposicionar. Clique abre o resumo. Botão direito: atualizar, abrir o painel, iniciar com o Windows, sair.

A posição fica em `%LOCALAPPDATA%\CursorMeter\offset.txt`.

## O que o .NET precisa de ter

- SDK .NET 8, para compilar.
- No Windows, o workload de ambiente de trabalho (`net8.0-windows` / Windows Forms), que vem com o SDK instalado no Windows.
- Não é preciso o Windows App SDK nem o WinUI do TaskbarQuota.

A lógica (sessão, pedido, texto das percentagens, posição na barra) corre em qualquer sistema com o SDK. A janela da barra de tarefas só existe no Windows.
