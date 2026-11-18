# As duas edições

O CursorMeter tem duas edições para Windows, com a mesma base de código:

| Edição | Versão | Para quem | O que faz de diferente |
|---|---|---|---|
| **Island** | estável, 1.3.x | Windows 11 com o [Start11 v3](https://www.stardock.com/products/start11/), ou sem Start11 | As barras são mais uma ilha da barra de tarefas. Corre sem permissões de administrador. |
| **Mainland** | beta, 1.4.0-beta.N | Windows 10 e 11, incluindo o Start11 v1 e v2 | Tudo o que a Island faz, mais um **modo administrador experimental** (UIAccess) que mantém as barras visíveis em cima da barra de tarefas. |

O nome vem das ilhas do Start11 v3. A Island vive nelas. A Mainland é para a barra de tarefas de sempre, o continente, sem ilhas.

## Qual escolher?

Com o Start11 v3 ou sem Start11, a Island. Com o Start11 v1 ou v2, a Mainland: nestas versões, abrir e fechar o menu Iniciar ou a Vista de Tarefas **com o rato** deixa a barra de tarefas por cima das barras do CursorMeter até ao próximo clique (com a tecla do Windows não acontece). O modo administrador experimental resolve isto. Ver [o modo administrador](mainland.md).

## Instalar uma ou outra

O instalador da Mainland, ao detetar um Start11 anterior ao v3, recomenda instalar com permissão de administrador (modo experimental). O da Island, nesse caso, avisa que a edição não é totalmente compatível e recomenda a Mainland (a partir da próxima versão estável).

A release traz um instalador de Windows de 64 bits por edição: `CursorMeter-Setup-<versão>.exe` (Island) e `CursorMeter-Mainland-Setup-<versão>.exe` (Mainland, marcada como pré-lançamento). As duas edições usam a mesma pasta e o mesmo registo de instalação: **instalar uma substitui a outra**, e nunca correm as duas ao mesmo tempo. Na Mainland, o ecrã **Acerca de** e o registo mostram a edição.
