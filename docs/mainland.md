# Modo administrador experimental (Mainland)

Só existe na edição Mainland (beta). A Island não tem este código e nunca se eleva. Ver [as duas edições](edicoes.md).

O Windows põe a barra de tarefas numa camada acima das janelas normais quando o Start11 anterior ao v3 a "levanta". Só um processo com `UIAccess` consegue ficar acima dessa camada. A Mainland cria esse estado como o [AlwaysOnTop](https://github.com/TopSoftdeveloper/AlwaysOnTop): corre elevada, copia o token do `winlogon.exe` da sessão e relança-se com `UIAccess` ativo. Por isso:

- **Pede permissão de administrador** (UAC) ao iniciar, ou ao instalar se escolheres o arranque com o Windows.
- **O arranque com o Windows usa uma Tarefa Agendada** (`CursorMeter`, privilégio mais alto) em vez de um atalho, para não pedir o UAC a cada início de sessão.
- **É experimental.** Se o UIAccess não ficar ativo, a app continua sem ele, como a Island, e o motivo fica no registo.

O modo ativa-se quando, no instalador, respondes Sim ao aviso do Start11 anterior ao v3. Desinstalar apaga a Tarefa Agendada. Para o desligar sem desinstalar, apaga `experimental-admin` na pasta do programa e a tarefa `CursorMeter` no Agendador de Tarefas.
