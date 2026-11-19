# Instruções para agentes

## PRIMEIRO PASSO OBRIGATÓRIO: confirma a edição

**Antes de qualquer outra ação** (ler ficheiros, pesquisar, investigar, correr comandos, editar), pergunta ao programador em qual das edições quer trabalhar. Isto aplica-se a **todas** as tarefas sobre este projeto: bugs, investigação, revisão, versões, README, instalador ou código. Só se dispensa a pergunta em conversas que não tocam no projeto.

1. Corre `git branch --show-current` e diz em que pasta e branch estás.
2. Usa a ferramenta de perguntas com as opções Island, Mainland e "as duas".
3. Não leias nem pesquises mais nada enquanto não houver resposta.
4. A resposta vale para o resto da conversa, até o programador mudar de edição. Mesmo assim, mantém a pergunta em cada conversa nova, mesmo que a pasta pareça indicar uma.

Este projeto tem **duas edições** do CursorMeter para Windows, cada uma na sua branch e, normalmente, na sua pasta de trabalho (`git worktree`):

| Edição | Branch | Pasta habitual | Para quem | Versões |
|---|---|---|---|---|
| **Island** (estável) | `main` | `cursor-meter-island` | Start11 v3 e barra nativa | `1.3.x` |
| **Mainland** (beta) | `mainland` | `cursor-meter` | Start11 v1/v2, com modo administrador experimental (UIAccess) | `1.4.0-rc.N` |

Se a pasta e a branch não corresponderem à edição pretendida, **não mudes de branch dentro da pasta**. Diz ao programador para abrir a pasta certa (ou propõe criar um `git worktree`).

## Regras para manter as edições separadas

- A Island (`main`) **nunca** recebe código da Mainland: nada de `UiAccess`, `RegionCutGuard`, instalador com modo administrador, nem a constante `MAINLAND`.
- As correções que valem para as duas fazem-se **primeiro na `main`** e entram na `mainland` com `git merge main`. Nunca ao contrário, e sem `cherry-pick` da `mainland` para a `main`, a menos que o programador peça.
- Uma tarefa que toca nas duas edições faz-se em dois passos, cada um na sua pasta e com o seu commit. Diz isso ao programador antes de começar.
- Não alteres nem apagues releases ou etiquetas existentes (`v1.3.1`, `v1.4.0-beta.1`, ...) sem o programador pedir. Cada release aponta para a branch da sua edição.
- Compila e testa sempre na pasta da edição em que estás a trabalhar (`dotnet build`, `dotnet test`). Cada pasta tem os seus `bin` e `obj`.
- O **instalador** não se gera na pasta da edição, mas na pasta de releases `cursor-meter-rel` (`C:\Users\Bruno\Documents\GitHub\cursor-meter-rel`, um `git worktree` em HEAD destacado), sempre a partir do commit da release. Assim o instalador nunca leva alterações soltas da pasta de trabalho. Gera-se localmente, não há CI. Ver "Publicar uma release".
- As duas edições partilham o AppId do instalador, o mutex `Local\CursorMeter` e `%LOCALAPPDATA%\CursorMeter`: instalar uma substitui a outra e nunca correm as duas ao mesmo tempo.

## Publicar uma release

Quando o programador pede para **publicar uma release**, isso inclui **fazer commit e push para a branch da respetiva edição** (`main` para a Island, `mainland` para a Mainland). Não precisa de pedir autorização separada para o commit. Passos, por esta ordem:

1. Confirma a edição (ver acima) e trabalha na pasta dessa edição.
2. Atualiza a versão (`.csproj` e `app.manifest`), compila e corre os testes na pasta da edição.
3. Faz commit só do que pertence à release e push para a branch da edição. Não incluas ficheiros alheios (por exemplo alterações soltas em `scripts\test-start-menu.ps1`).
4. Gera o instalador na pasta de releases `cursor-meter-rel`:
   - Confirma que `git -C ..\cursor-meter-rel status --short` está vazio. Se não estiver, pára e pergunta ao programador.
   - Põe-na no commit da release, sem criar branch: `git -C ..\cursor-meter-rel checkout --detach <branch>` (`main` ou `mainland`). Esta é a única pasta onde se muda de commit.
   - Corre `scripts\build-installer.ps1` dentro dela. O instalador fica em `cursor-meter-rel\dist\CursorMeter-Setup-<versão>.exe`.
5. Cria a etiqueta e a release no GitHub a apontar para esse commit (`gh release create ... --target <branch>`), com as notas em português e o instalador de `cursor-meter-rel\dist` como anexo:
   - **Island:** etiqueta `v1.3.x`, release normal, branch `main`.
   - **Mainland:** etiqueta `v1.4.0-rc.N`, `--prerelease --latest=false`, branch `mainland`.
6. Nas notas, diz o que foi testado e o que não foi.

Se a versão pedida já existir como release, não a substituas: pergunta ao programador.

## Contexto útil

- A beta existe por causa de um problema do Start11 anterior ao v3: depois de abrir e fechar o menu Iniciar ou a Vista de Tarefas com o rato, a barra de tarefas fica numa camada acima das barras. O modo UIAccess (token do `winlogon.exe`, como o AlwaysOnTop) resolve-o, à custa de correr como administrador.
- O README descreve as duas edições para os utilizadores. Mantém-no coerente com a branch em que estás.
