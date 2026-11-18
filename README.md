<p align="center">
  <img src="assets/logo.png" alt="Logótipo do CursorMeter" width="128">
</p>

# CursorMeter Mainland (beta)

A edição do CursorMeter para quem usa o **Start11 v1 ou v2** no Windows. Mostra as mesmas duas barras de consumo do Cursor que a edição Island e acrescenta um **modo administrador experimental** que as mantém por cima da barra de tarefas.

**[Ver as releases](https://github.com/brunorochaipca/cursor-meter/releases)** · Windows 10 e 11 · versão beta 1.4.0-beta.4 · [MIT](LICENSE)

Esta é uma versão de testes. A edição estável é a **Island**, na [branch `main`](https://github.com/brunorochaipca/cursor-meter/blob/main/README.md).

## Porque existe

Nestas versões do Start11, depois de abrir e fechar o menu Iniciar ou a Vista de Tarefas **com o rato**, a barra de tarefas fica por cima das barras do CursorMeter até ao próximo clique (com a tecla do Windows não acontece). O modo administrador experimental (UIAccess) resolve-o. Com o Start11 v3 ou sem Start11, usa a Island.

## Instalar

1. Nas [releases](https://github.com/brunorochaipca/cursor-meter/releases), transfere `CursorMeter-Mainland-Setup-<versão>.exe` (está marcada como pré-lançamento).
2. Corre-o. O Windows pode dizer que o publicador é desconhecido, porque o instalador não está assinado: escolhe **Mais informações** e depois **Executar mesmo assim**.
3. No passo **Selecione o modo de instalação**, escolhe se o CursorMeter pede permissão de administrador. Se for detetado um Start11 anterior ao v3, esse passo recomenda o modo administrador e explica porquê. Com essa opção, o botão **Instalar** mostra o escudo do UAC e pede a permissão.
4. Se o CursorMeter Mainland já estiver instalado, esse passo só oferece **Atualizar** (mantém o modo atual, sem pedir permissão) ou **Desinstalar primeiro** a versão instalada, para poderes escolher outro modo.

## Cuidados

- **Pede permissão de administrador** (UAC) uma vez, ao clicar em Instalar. A permissão fica numa Tarefa Agendada, e o CursorMeter arranca por ela sem voltar a pedir o UAC, ao abri-lo ou ao iniciar sessão.
- **É experimental.** Se o UIAccess não ficar ativo, a app continua sem ele, como a Island, e o motivo fica no registo.
- **Instalar a Mainland substitui a Island**, e vice-versa. Nunca correm as duas ao mesmo tempo.
- **Para desligar o modo** sem desinstalar: apaga `experimental-admin` na pasta do programa e a tarefa `CursorMeter` no Agendador de Tarefas.

Como funciona por dentro: [modo administrador experimental](docs/mainland.md).

## Tudo o resto

As definições, as ilhas, as barras flutuantes, a privacidade e o Ubuntu são iguais aos da Island. Ver o [README da Island](https://github.com/brunorochaipca/cursor-meter/blob/main/README.md) e a pasta [docs](docs):

- [Windows e Start11](docs/windows.md)
- [As duas edições](docs/edicoes.md)
- [Resolução de problemas](docs/resolucao-de-problemas.md)
- [Compilar a partir do código](docs/compilar.md)

## Licença

[MIT](LICENSE). O Avalonia, a interface, tem a sua própria licença MIT.
