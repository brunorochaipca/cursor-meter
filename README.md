# CursorMeter

Duas barras na barra de tarefas do Windows, só para o Cursor:

`37% Cursor Models | 64% Other Models`

Cada barra é a quota desse pool no ciclo atual. Cursor Models corresponde a `autoPercentUsed`. Other Models corresponde a `apiPercentUsed`. São as mesmas duas percentagens do painel Usage.

Lê a sessão que o próprio Cursor gravou ao iniciares sessão, em `%APPDATA%\Cursor\User\globalStorage\state.vscdb`. Se essa sessão expirar, usa a sessão de cursor.com que estiver no Firefox.

## Instalar

A release traz um instalador para Windows de 64 bits. Não pede administrador: o programa fica em `%LOCALAPPDATA%\Programs\CursorMeter` e aparece em Aplicações instaladas. A posição das barras continua em `%LOCALAPPDATA%\CursorMeter\offset.txt`, por isso uma reinstalação não a apaga.

O Windows pode dizer que o publicador é desconhecido, porque o instalador não está assinado. No assistente podes deixar marcado o arranque com o Windows. O mesmo interruptor está no menu do botão direito.

## Ubuntu

O pacote para Ubuntu de 64 bits é `CursorMeter-<versão>-linux-x64.tar.gz`. Não pede sudo nem precisa do .NET instalado:

```bash
tar -xzf CursorMeter-<versão>-linux-x64.tar.gz
cd CursorMeter-<versão>-linux-x64
./install.sh --autostart
```

O programa fica em `~/.local/share/cursormeter`, a entrada nas aplicações em `~/.local/share/applications` e o ícone em `~/.local/share/icons/hicolor/scalable/apps`. Com `--autostart` arranca com a sessão. O mesmo interruptor está no menu, "Iniciar com a sessão", e grava `~/.config/autostart/cursormeter.desktop`. Para desinstalar: `~/.local/share/cursormeter/uninstall.sh`.

No Ubuntu não há barras dentro do painel. Na área de notificação fica o ícone e, à direita dele, as duas percentagens em texto, por exemplo `Cursor 9% · Outros 43%`, arredondadas como as barras do Windows. Enquanto lê, aparece "A ler…". Se falhar antes de ter quotas, aparece o erro, por exemplo "Abre o Cursor" ou "Sem ligação". O texto atualiza-se de 3 em 3 minutos e em "Atualizar". O resumo abre no canto, junto ao painel, e fecha com Esc ou com um clique noutra janela.

O menu tem sempre, por esta ordem: Mostrar resumo, Atualizar, Painel do Cursor, Iniciar com a sessão, Sair. O que cada clique faz depende do painel:

- GNOME (extensão Ubuntu AppIndicators): o clique simples abre o menu, porque a extensão abre sempre o menu. O duplo clique e o botão do meio abrem e fecham o resumo. O botão direito abre o menu. Um clique na barra superior não fecha o resumo, e um clique no ambiente de trabalho só o fecha se os ícones do ambiente de trabalho estiverem ativos.
- Xfce, plugin Área de notificação: o clique abre e fecha o resumo, tal como o botão do meio. O botão direito abre o menu. Este plugin não mostra o texto, só o ícone. As percentagens ficam na dica do ícone.
- Xfce, plugin Indicadores (com `ayatana-indicator-application`): mostra o texto. O clique abre o menu e o botão do meio abre e fecha o resumo. O botão direito abre o menu do próprio painel.
- KDE Plasma: mostra o ícone, mas não o texto.

O ícone precisa de um painel com suporte para AppIndicator (StatusNotifierItem). O Ubuntu traz a extensão Ubuntu AppIndicators ativa por omissão. Noutras instalações do GNOME, instala `gnome-shell-extension-appindicator` e ativa-a nas Extensões. Xfce e KDE já mostram estes ícones.

A sessão vem de `~/.config/Cursor/User/globalStorage/state.vscdb`, gravada pelo Cursor, ou de `~/.config/cursor/auth.json`, gravada pelo Cursor CLI. Se `XDG_CONFIG_HOME` estiver definido, substitui `~/.config`. Se a sessão expirar, usa a de cursor.com no Firefox, procurada em `~/.mozilla/firefox`, `~/snap/firefox/common/.mozilla/firefox` (o Firefox snap do Ubuntu), `~/.var/app/org.mozilla.firefox/.mozilla/firefox` (Flatpak) e `~/.config/mozilla/firefox`.

Para compilar, com o .NET 8 SDK:

```bash
./scripts/build-linux.sh
```

O pacote sai em `dist/CursorMeter-<versão>-linux-x64.tar.gz`. O executável fica em `src/CursorMeter.App/bin/Release/net8.0/linux-x64/publish/CursorMeter`.

## Correr a partir do código

No Windows, com o [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) e o [Inno Setup 6](https://jrsoftware.org/isdl.php) se fores gerar o instalador:

```powershell
.\scripts\build-windows.ps1
.\src\CursorMeter.App\bin\Release\net8.0\win-x64\publish\CursorMeter.exe
```

O instalador, `dist\CursorMeter-Setup-<versão>.exe`, sai de `.\scripts\build-installer.ps1`.

O executável fica ao lado do relógio, na barra de tarefas. Arrasta para o reposicionar. Clique abre o resumo. Botão direito: atualizar, abrir o painel, iniciar com o Windows, sair.

No Windows 10 as barras ficam dentro da barra. No Windows 11 (o 26H2 incluído) o sistema não mostra uma janela metida na barra: à escala de 100% não aparece nada, e à de 125% só aparece porque o Windows a amplia. As barras são desenhadas por cima da barra, no mesmo sítio, e seguem a escala do monitor. Se numa máquina não aparecerem, `.\scripts\debug-taskbar.ps1` diz a escala, o DPI da barra e se a janela está por baixo dela.

A posição fica em `%LOCALAPPDATA%\CursorMeter\offset.txt`.

## O que o .NET precisa de ter

- SDK .NET 8, para compilar. O projeto aponta para `net8.0`, sem workloads de Windows.
- A interface é [Avalonia](https://avaloniaui.net/) 11.3, que vem do NuGet. O Avalonia 12 exige uma SDK .NET 9.0.300 ou mais recente para compilar, por isso fica de fora enquanto o `global.json` fixar a SDK 8.
- Não é preciso o Windows App SDK nem o WinUI do TaskbarQuota.

A lógica (sessão, pedido, texto das percentagens, posição na barra) está em `CursorMeter.Core` e corre em qualquer sistema. O resumo e o menu são Avalonia e também correm em qualquer sistema. No Windows, o ícone na área de notificação também é Avalonia. No Linux, como o ícone do Avalonia não aceita texto, o ícone é um StatusNotifierItem próprio, em `src/CursorMeter.App/Linux`, e só volta ao do Avalonia se não houver sessão D-Bus. As barras dentro da barra de tarefas são Win32 e só existem no Windows (`src/CursorMeter.App/Windows`). O arranque com a sessão no Linux está em `src/CursorMeter.App/Linux`.
