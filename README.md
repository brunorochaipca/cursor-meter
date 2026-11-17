<p align="center">
  <img src="assets/logo.png" alt="Logótipo do CursorMeter" width="128">
</p>

# CursorMeter

O consumo do Cursor sempre à vista: duas barras, uma por pool de quota, sem abrires o painel Usage.

![A ilha do CursorMeter ao lado das ilhas do Start11 v3, no Windows 11](assets/taskbar.png)

Cada barra é a quota desse pool no ciclo atual. Cursor Models corresponde a `autoPercentUsed`. Other Models corresponde a `apiPercentUsed`. São as mesmas duas percentagens do painel Usage.

Lê a sessão que o próprio Cursor gravou ao iniciares sessão, em `%APPDATA%\Cursor\User\globalStorage\state.vscdb`. Se essa sessão expirar, usa a sessão de cursor.com que estiver no Firefox. Não envia nada para além do pedido de uso ao próprio Cursor.

## Para quem é

No **Windows**, a aplicação foi pensada para o Windows 11 com o [Start11 v3](https://www.stardock.com/products/start11/), da Stardock. O Start11 v3 trouxe as "ilhas" da barra de tarefas: em vez de uma faixa de lado a lado, a barra passa a blocos arredondados, como num dock. O CursorMeter replica esse conceito e apresenta-se como mais uma ilha, com o mesmo aspeto das que o Start11 desenha.

No **Ubuntu**, é um ícone na área de notificação com as percentagens ao lado. Ver [Ubuntu](#ubuntu).

## Windows

### A configuração pensada

No Start11 v3, ativa as ilhas da barra de tarefas (Taskbar Islands) e usa o alinhamento **tudo ao centro**: o botão Iniciar e os ícones juntos numa ilha ao centro, e a área de notificação noutra, à direita. O espaço livre à esquerda fica para o CursorMeter, que ocupa o **canto esquerdo** como uma ilha própria e fica sempre fixo ali.

```
┌──────────────┐      ┌───────────────────────┐      ┌──────────────┐
│  CursorMeter │      │  Iniciar e ícones     │      │ Notificações │
└──────────────┘      └───────────────────────┘      └──────────────┘
  canto esquerdo           ao centro                    à direita
```

A ilha:

- **Encaixa sozinha.** A aplicação lê a forma que o Start11 dá à barra de tarefas (a região da janela) e deduz onde estão as ilhas, a altura e o raio dos cantos. Não usa valores fixos para o zoom ou a resolução, por isso acompanha o zoom de cada monitor. A margem à esquerda é igual à que a ilha da direita tem à direita.
- **Usa as cores do Start11.** O preenchimento, a cor e a espessura do contorno (1, 1,25, 1,5 ou 2 px) vêm das definições da barra de tarefas do Start11. Se as mudares, a ilha acompanha em poucos segundos. O contorno é suavizado e misturado com o fundo, como o do Start11.
- **Fica sempre visível.** A ilha não é tapada ao abrir o menu Iniciar do Start11, ao clicar num ícone da barra ou com uma janela maximizada. Só a Vista de Tarefas (Win+Tab) a tapa, como a qualquer outra janela, e volta logo ao fechar.
- **Fica fixa.** Não se arrasta. Clique abre o resumo, junto à ilha. Botão direito: atualizar, abrir o painel, iniciar com o Windows, sair.

Testado com o Start11 v3 beta, no Windows 11, a 125% de zoom, com a barra em baixo. A barra no topo ou vertical não usa o modo ilha.

### Outros alinhamentos do Start11

O Start11 também deixa alinhar os ícones à esquerda ou só os botões ao centro. A ilha das barras procura sempre espaço livre entre as ilhas do Start11, por esta ordem:

- **Tudo ao centro:** canto esquerdo, como acima.
- **Botões ao centro:** ao lado da ilha do Iniciar, entre ela e a ilha dos botões.
- **Alinhado à esquerda:** entre a ilha dos ícones e a área de notificação, encostada a esta.

Se o primeiro espaço não chegar, usa o maior que couber. Se nenhum couber, as barras flutuam (ver abaixo). Os estilos Sólido e Arredondado do Start11 não têm ilhas separadas e mantêm o comportamento da 1.2.6.

### Quando a ilha das janelas ocupa o espaço

Com muitas janelas abertas, a ilha dos ícones do Start11 cresce para a esquerda e deixa de haver espaço para a ilha das barras. Em vez de ficarem por cima dos ícones, as barras **flutuam por cima da barra de tarefas**, no canto esquerdo, mantendo o aspeto de ilha:

- **Esbatidas.** Ficam a cerca de 40% de opacidade e só ficam totalmente visíveis quando o rato passa por cima, para tapar o menos possível.
- **Arrastáveis.** Aqui podem-se arrastar para qualquer ponto do ecrã, também para outro monitor. A posição fica em `%LOCALAPPDATA%\CursorMeter\floating.txt`. Se esse monitor desaparecer, voltam ao canto esquerdo.
- **Escondíveis.** No menu (botão direito nas barras ou no ícone da área de notificação), "Esconder as barras flutuantes". Só vale enquanto não houver espaço para a ilha.
- **Voltam sozinhas.** Quando fechas janelas e a ilha volta a caber, as barras regressam ao canto da barra de tarefas. Pedem uns pixéis a mais para voltar do que para sair, para não oscilarem.

### Definições

Botão direito nas barras ou no ícone da área de notificação, **Definições…**. A janela é parecida com a do Start11: secções à esquerda, um cartão por definição e, em cima, uma pré-visualização das barras que acompanha cada alteração. Tudo se aplica logo e fica em `%LOCALAPPDATA%\CursorMeter\settings.txt`.

- **Aspeto:** tema (Automático, Escuro, **Transparente** ou Seguir o Start11, que no estilo Arredondado ou Sólido deixa as barras diretamente na barra, sem caixa), opacidade do fundo, contorno, cantos, tamanho do texto, mostrar uma só pool e mostrar o que resta em vez do que usaste.
- **Cores:** texto, texto secundário, as duas pools, aviso, crítico e contorno. Para o tema transparente, escolhe um texto claro ou escuro conforme a tua barra de tarefas.
- **Utilização e alertas:** de quanto em quanto tempo lê a quota (1, 3, 5 ou 10 minutos) e a partir de que valor a barra passa a aviso (70% por omissão) e a crítico (90%).
- **Barras flutuantes:** opacidade em repouso, esconder e repor a posição.
- **Geral:** iniciar com o Windows e repor todas as definições.

O tema, a opacidade, o contorno e os cantos valem só **fora do modo ilha**: na caixa junto ao relógio, no Windows 10 e quando as barras flutuam. Como ilha da barra de tarefas, as barras seguem sempre as cores do Start11. O tema transparente não foi testado no Windows 10.

### Sem o Start11 v3, ou sem ilhas

A aplicação continua a funcionar, com o comportamento anterior. Se as ilhas estiverem desligadas (estilo Sólido ou Arredondado), se o Start11 não estiver instalado, ou se não houver espaço, as barras aparecem numa caixa ao lado da área de notificação, junto ao relógio.

- **Windows 10:** as barras ficam dentro da barra de tarefas.
- **Windows 11:** o sistema não mostra uma janela metida na barra: à escala de 100% não aparece nada, e à de 125% só aparece porque o Windows a amplia. As barras são desenhadas por cima da barra, no mesmo sítio, e seguem a escala do monitor.
- **Menu Iniciar e Vista de Tarefas:** com o menu Iniciar do Windows aberto, ou com a Vista de Tarefas, o Windows põe a barra por cima de qualquer janela normal. As barras ficam no mesmo sítio: a área que ocupam deixa de fazer parte da barra enquanto o menu está aberto, e volta ao normal quando fecha. Um clique num ícone da barra também não as esconde.
- **Posição:** arrasta para reposicionar. Fica em `%LOCALAPPDATA%\CursorMeter\offset.txt`.

Se numa máquina não aparecerem, abre **Acerca de** no menu do botão direito: mostra a versão e o ficheiro de registos (`%LOCALAPPDATA%\CursorMeter\cursormeter.log`), que diz como as barras foram colocadas. O botão **Enviar registos** copia o registo e abre uma issue no GitHub.

### Instalar

A release traz um instalador para Windows de 64 bits. Não pede administrador: o programa fica em `%LOCALAPPDATA%\Programs\CursorMeter` e aparece em Aplicações instaladas. A posição das barras, quando não estás no modo ilha, continua em `%LOCALAPPDATA%\CursorMeter\offset.txt`, por isso uma reinstalação não a apaga.

O Windows pode dizer que o publicador é desconhecido, porque o instalador não está assinado. No assistente podes deixar marcado o arranque com o Windows. O mesmo interruptor está no menu do botão direito.

O Start11 é um produto da Stardock, pago e independente deste projeto. O CursorMeter não o inclui nem tem qualquer ligação à Stardock.

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

As barras aparecem na barra de tarefas, como descrito em [Windows](#windows).

## O que o .NET precisa de ter

- SDK .NET 8, para compilar. O projeto aponta para `net8.0`, sem workloads de Windows.
- A interface é [Avalonia](https://avaloniaui.net/) 11.3, que vem do NuGet. O Avalonia 12 exige uma SDK .NET 9.0.300 ou mais recente para compilar, por isso fica de fora enquanto o `global.json` fixar a SDK 8.
- Não é preciso o Windows App SDK nem o WinUI do TaskbarQuota.

A lógica (sessão, pedido, texto das percentagens, posição na barra, deteção das ilhas) está em `CursorMeter.Core` e corre em qualquer sistema. A leitura das cores do Start11 está em `src/CursorMeter.App/Windows/Start11Theme.cs`. O resumo e o menu são Avalonia e também correm em qualquer sistema. No Windows, o ícone na área de notificação também é Avalonia. No Linux, como o ícone do Avalonia não aceita texto, o ícone é um StatusNotifierItem próprio, em `src/CursorMeter.App/Linux`, e só volta ao do Avalonia se não houver sessão D-Bus. As barras dentro da barra de tarefas são Win32 e só existem no Windows (`src/CursorMeter.App/Windows`). O arranque com a sessão no Linux está em `src/CursorMeter.App/Linux`.

## Licença

[MIT](LICENSE). O Avalonia, a interface, tem a sua própria licença MIT.
