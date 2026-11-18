# Instruções para agentes

## Antes de mexer em código: confirma a edição

Este projeto tem **duas edições** do CursorMeter para Windows, cada uma na sua branch e, normalmente, na sua pasta de trabalho (`git worktree`):

| Edição | Branch | Pasta habitual | Para quem | Versões |
|---|---|---|---|---|
| **Island** (estável) | `main` | `cursor-meter-island` | Start11 v3 e barra nativa | `1.3.x` |
| **Mainland** (beta) | `mainland` | `cursor-meter` | Start11 v1/v2, com modo administrador experimental (UIAccess) | `1.4.0-beta.N` |

**No início de cada tarefa que altere código, scripts, instalador, README ou versões, pergunta ao programador em qual das edições quer trabalhar, mesmo que a pasta atual pareça indicar uma.** Faz a pergunta antes de editar qualquer ficheiro. Usa `git branch --show-current` e o caminho da pasta para dizer em que edição estás agora, e pede confirmação (por exemplo com a ferramenta de perguntas, com as opções Island, Mainland e "as duas").

Se a pasta e a branch não corresponderem à edição pretendida, **não mudes de branch dentro da pasta**. Diz ao programador para abrir a pasta certa (ou propõe criar um `git worktree`).

## Regras para manter as edições separadas

- A Island (`main`) **nunca** recebe código da Mainland: nada de `UiAccess`, `RegionCutGuard`, instalador com modo administrador, nem a constante `MAINLAND`.
- As correções que valem para as duas fazem-se **primeiro na `main`** e entram na `mainland` com `git merge main`. Nunca ao contrário, e sem `cherry-pick` da `mainland` para a `main`, a menos que o programador peça.
- Uma tarefa que toca nas duas edições faz-se em dois passos, cada um na sua pasta e com o seu commit. Diz isso ao programador antes de começar.
- Não alteres nem apagues releases ou etiquetas existentes (`v1.3.1`, `v1.4.0-beta.1`, ...) sem o programador pedir. Cada release aponta para a branch da sua edição.
- Compila e testa sempre na pasta da edição em que estás a trabalhar (`dotnet test`, `scripts\build-installer.ps1`). Cada pasta tem os seus `bin` e `obj`.
- As duas edições partilham o AppId do instalador, o mutex `Local\CursorMeter` e `%LOCALAPPDATA%\CursorMeter`: instalar uma substitui a outra e nunca correm as duas ao mesmo tempo.

## Contexto útil

- A beta existe por causa de um problema do Start11 anterior ao v3: depois de abrir e fechar o menu Iniciar ou a Vista de Tarefas com o rato, a barra de tarefas fica numa camada acima das barras. O modo UIAccess (token do `winlogon.exe`, como o AlwaysOnTop) resolve-o, à custa de correr como administrador.
- O README descreve as duas edições para os utilizadores. Mantém-no coerente com a branch em que estás.
