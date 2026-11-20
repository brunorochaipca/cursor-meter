# Zorin OS

Na Mainland, a versão Linux é feita para o **Zorin OS** (18, GNOME 46 com a Zorin Taskbar). A Zorin Taskbar deixa aumentar a altura da barra de tarefas, e é isso que permite desenhar nela as duas barras, como no Windows.

O pacote de 64 bits é `CursorMeter-<versão>-linux-x64.tar.gz`. Não pede sudo nem precisa do .NET instalado:

```bash
tar -xzf CursorMeter-<versão>-linux-x64.tar.gz
cd CursorMeter-<versão>-linux-x64
./install.sh --autostart
```

O programa fica em `~/.local/share/cursormeter`, a entrada nas aplicações em `~/.local/share/applications` e o ícone em `~/.local/share/icons/hicolor/scalable/apps`. Com `--autostart` arranca com a sessão. O mesmo interruptor está no menu, "Iniciar com a sessão", e grava `~/.config/autostart/cursormeter.desktop`. Para desinstalar: `~/.local/share/cursormeter/uninstall.sh`.

O instalador também instala e ativa a extensão do GNOME Shell `cursormeter@cursor-meter` (sem apagar as que já tens ativas). **Termina a sessão e entra outra vez** para o GNOME a carregar. No Wayland não há outra forma.

## O que aparece

A extensão desenha na barra de tarefas as duas barras (Cursor e Outros), uma por cima da outra, com o nome, a barra e a percentagem. O programa lê as quotas, e a extensão só as mostra: o programa grava `$XDG_RUNTIME_DIR/cursormeter/bars.json` e a extensão desenha o que lá está. O tamanho acompanha a altura da barra de tarefas. Enquanto lê, aparece "A ler…". Se falhar antes de ter quotas, aparece o erro, por exemplo "Abre o Cursor" ou "Sem ligação". Atualiza-se de 3 em 3 minutos e em "Atualizar".

Um clique nas barras abre o resumo, centrado por cima delas. Fecha com Esc ou com um clique noutra janela. O menu tem, por esta ordem: Mostrar resumo, Atualizar, Painel do Cursor, Definições, Iniciar com a sessão, Sair.

Nas definições, a página **Aspeto** tem o tamanho do texto, o espaço entre as barras e a cor do texto.

## Sem a extensão

Se a extensão não estiver ativa, o programa volta ao ícone na área de notificação com as percentagens em texto, por exemplo `Cursor 9% · Outros 43%`. O resumo abre no canto, junto ao painel. O ícone precisa de um painel com suporte para AppIndicator (StatusNotifierItem). O Zorin traz-o ativo (`zorin-appindicator`). Noutros ambientes (outras instalações do GNOME, Xfce, KDE) o ícone pode aparecer, mas não são testados.

## Onde procura a sessão

A sessão vem de `~/.config/Cursor/User/globalStorage/state.vscdb`, gravada pelo Cursor, ou de `~/.config/cursor/auth.json`, gravada pelo Cursor CLI. Se `XDG_CONFIG_HOME` estiver definido, substitui `~/.config`. Se a sessão expirar, usa a de cursor.com no Firefox, procurada em `~/.mozilla/firefox`, `~/snap/firefox/common/.mozilla/firefox` (Firefox snap), `~/.var/app/org.mozilla.firefox/config/mozilla/firefox` e `~/.var/app/org.mozilla.firefox/.mozilla/firefox` (Flatpak, a primeira nas versões recentes) e `~/.config/mozilla/firefox`.

## Compilar

Ver [compilar a partir do código](compilar.md).
