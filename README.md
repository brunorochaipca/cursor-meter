<p align="center">
  <img src="assets/logo.png" alt="Logótipo do CursorMeter" width="128">
</p>

# CursorMeter

Vê o consumo do Cursor sem abrir o painel Usage: duas barras discretas, uma por pool de quota, sempre à vista.

**[Transferir a última versão](https://github.com/brunorochaipca/cursor-meter/releases/latest)** · Windows 10 e 11 · Zorin OS · [MIT](LICENSE)

## O que mostra

- **Cursor Models:** a quota do ciclo atual para esse pool (`autoPercentUsed`).
- **Other Models:** a quota do ciclo atual para os restantes modelos (`apiPercentUsed`).

São as mesmas duas percentagens do painel Usage. Clica nas barras para ver o resumo, e usa o botão direito para atualizar, abrir o painel ou mudar as definições.

## Instalar

### Windows

1. Transfere o instalador (`.exe`) da [última release](https://github.com/brunorochaipca/cursor-meter/releases/latest).
2. Corre-o. O Windows pode dizer que o publicador é desconhecido, porque o instalador não está assinado: escolhe **Mais informações** e depois **Executar mesmo assim**.
3. No passo **Selecione o modo de instalação**, escolhe se o CursorMeter pede permissão de administrador (ver abaixo). Se for detetado um Start11 anterior ao v3, esse passo recomenda o modo administrador e explica porquê.
4. Garante que o Cursor tem a sessão iniciada. As barras aparecem sozinhas na barra de tarefas.

Se o CursorMeter já estiver instalado, esse passo só oferece **Atualizar** (mantém o modo atual, sem pedir permissão) ou **Desinstalar primeiro**, para poderes escolher outro modo.

### Zorin OS

Pacote de 64 bits, sem sudo e sem precisar do .NET. Ver [Zorin OS](docs/zorin.md).

## Modo administrador (experimental)

Com o **Start11 v1 ou v2**, depois de abrir e fechar o menu Iniciar ou a Vista de Tarefas **com o rato**, a barra de tarefas fica por cima das barras do CursorMeter até ao próximo clique (com a tecla do Windows não acontece). O modo administrador experimental (UIAccess) resolve-o. Com o Start11 v3 ou sem Start11 não é preciso.

- **Pede permissão de administrador** (UAC) uma vez, ao clicar em Instalar. A permissão fica numa Tarefa Agendada, e o CursorMeter arranca por ela sem voltar a pedir o UAC, ao abri-lo ou ao iniciar sessão.
- **É experimental.** Se o UIAccess não ficar ativo, a app continua sem ele, e o motivo fica no registo.
- **Para desligar o modo** sem desinstalar: apaga `experimental-admin` na pasta do programa e a tarefa `CursorMeter` no Agendador de Tarefas.

Como funciona por dentro: [modo administrador experimental](docs/mainland.md).

## Privacidade

Lê a sessão que o próprio Cursor gravou ao iniciares sessão, em `%APPDATA%\Cursor\User\globalStorage\state.vscdb`. Se essa sessão expirar, usa a sessão de cursor.com que estiver no Firefox. Não envia nada para além do pedido de uso ao próprio Cursor.

## Algo não aparece?

Botão direito nas barras, **Acerca de**, **Enviar registos**: copia o registo e abre uma issue no GitHub. Mais ajuda em [resolução de problemas](docs/resolucao-de-problemas.md).

## Saber mais

- [Windows e Start11](docs/windows.md): ilhas, barras flutuantes, definições e modo sem ilhas.
- [Zorin OS](docs/zorin.md): a extensão do GNOME Shell, cliques e onde procura a sessão.
- [Resolução de problemas](docs/resolucao-de-problemas.md)
- [Compilar a partir do código](docs/compilar.md)

O Start11 é um produto da Stardock, pago e independente deste projeto. O CursorMeter não o inclui nem tem qualquer ligação à Stardock.

## Licença

[MIT](LICENSE). O Avalonia, a interface, tem a sua própria licença MIT.
