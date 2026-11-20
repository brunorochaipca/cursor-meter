# Compilar a partir do código

## Windows

Com o [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) e o [Inno Setup 6](https://jrsoftware.org/isdl.php) se fores gerar o instalador:

```powershell
.\scripts\build-windows.ps1
```

O executável fica em `src\CursorMeter.App\bin\Release\net8.0\win-x64\publish\CursorMeter.exe`. Para o instalador, corre `.\scripts\build-installer.ps1`: sai em `dist\CursorMeter-Setup-<versão>.exe`.

## Zorin OS

Com o .NET 8 SDK:

```bash
./scripts/build-linux.sh
```

O pacote sai em `dist/CursorMeter-<versão>-linux-x64.tar.gz`. O executável fica em `src/CursorMeter.App/bin/Release/net8.0/linux-x64/publish/CursorMeter`.

## O que o .NET precisa de ter

- SDK .NET 8, para compilar. O projeto aponta para `net8.0`, sem workloads de Windows.
- A interface é [Avalonia](https://avaloniaui.net/) 11.3, que vem do NuGet. O Avalonia 12 exige uma SDK .NET 9.0.300 ou mais recente para compilar, por isso fica de fora enquanto o `global.json` fixar a SDK 8.
- Não é preciso o Windows App SDK nem o WinUI do TaskbarQuota.

## Como o código está organizado

A lógica (sessão, pedido, texto das percentagens, posição na barra, deteção das ilhas) está em `CursorMeter.Core` e corre em qualquer sistema. A leitura das cores do Start11 está em `src/CursorMeter.App/Windows/Start11Theme.cs`. O resumo e o menu são Avalonia e também correm em qualquer sistema. No Windows, o ícone na área de notificação também é Avalonia. No Linux, como o ícone do Avalonia não aceita texto, o ícone é um StatusNotifierItem próprio, em `src/CursorMeter.App/Linux`, e só volta ao do Avalonia se não houver sessão D-Bus. As barras dentro da barra de tarefas são Win32 e só existem no Windows (`src/CursorMeter.App/Windows`). O arranque com a sessão no Linux está em `src/CursorMeter.App/Linux`.
