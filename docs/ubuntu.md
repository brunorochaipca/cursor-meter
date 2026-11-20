# Ubuntu

O pacote para Ubuntu de 64 bits é `CursorMeter-<versão>-linux-x64.tar.gz`. Não pede sudo nem precisa do .NET instalado:

```bash
tar -xzf CursorMeter-<versão>-linux-x64.tar.gz
cd CursorMeter-<versão>-linux-x64
./install.sh --autostart
```

O programa fica em `~/.local/share/cursormeter`, a entrada nas aplicações em `~/.local/share/applications` e o ícone em `~/.local/share/icons/hicolor/scalable/apps`. Com `--autostart` arranca com a sessão. O mesmo interruptor está no menu, "Iniciar com a sessão", e grava `~/.config/autostart/cursormeter.desktop`. Para desinstalar: `~/.local/share/cursormeter/uninstall.sh`.

## O que aparece

No Ubuntu não há barras dentro do painel. Na área de notificação fica o ícone e, à direita dele, as duas percentagens em texto, por exemplo `Cursor 9% · Outros 43%`, arredondadas como as barras do Windows. Enquanto lê, aparece "A ler…". Se falhar antes de ter quotas, aparece o erro, por exemplo "Abre o Cursor" ou "Sem ligação". O texto atualiza-se de 3 em 3 minutos e em "Atualizar". O resumo abre no canto, junto ao painel, e fecha com Esc ou com um clique noutra janela.

O menu tem sempre, por esta ordem: Mostrar resumo, Atualizar, Painel do Cursor, Iniciar com a sessão, Sair. O que cada clique faz depende do painel:

- **GNOME** (extensão Ubuntu AppIndicators): o clique simples abre o menu, porque a extensão abre sempre o menu. O duplo clique e o botão do meio abrem e fecham o resumo. O botão direito abre o menu. Um clique na barra superior não fecha o resumo, e um clique no ambiente de trabalho só o fecha se os ícones do ambiente de trabalho estiverem ativos.
- **Xfce, plugin Área de notificação:** o clique abre e fecha o resumo, tal como o botão do meio. O botão direito abre o menu. Este plugin não mostra o texto, só o ícone. As percentagens ficam na dica do ícone.
- **Xfce, plugin Indicadores** (com `ayatana-indicator-application`): mostra o texto. O clique abre o menu e o botão do meio abre e fecha o resumo. O botão direito abre o menu do próprio painel.
- **KDE Plasma:** mostra o ícone, mas não o texto.

O ícone precisa de um painel com suporte para AppIndicator (StatusNotifierItem). O Ubuntu traz a extensão Ubuntu AppIndicators ativa por omissão. Noutras instalações do GNOME, instala `gnome-shell-extension-appindicator` e ativa-a nas Extensões. Xfce e KDE já mostram estes ícones.

## Onde procura a sessão

A sessão vem de `~/.config/Cursor/User/globalStorage/state.vscdb`, gravada pelo Cursor, ou de `~/.config/cursor/auth.json`, gravada pelo Cursor CLI. Se `XDG_CONFIG_HOME` estiver definido, substitui `~/.config`. Se a sessão expirar, usa a de cursor.com no Firefox, procurada em `~/.mozilla/firefox`, `~/snap/firefox/common/.mozilla/firefox` (o Firefox snap do Ubuntu), `~/.var/app/org.mozilla.firefox/config/mozilla/firefox` e `~/.var/app/org.mozilla.firefox/.mozilla/firefox` (Flatpak, a primeira nas versões recentes) e `~/.config/mozilla/firefox`.

## Compilar

Ver [compilar a partir do código](compilar.md).
