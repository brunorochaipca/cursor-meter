# Modo administrador experimental (Mainland)

Só existe na edição Mainland (beta). A Island não tem este código e nunca se eleva. Ver [as duas edições](edicoes.md).

O Windows põe a barra de tarefas numa camada acima das janelas normais quando o Start11 anterior ao v3 a "levanta". Só um processo com `UIAccess` consegue ficar acima dessa camada. A Mainland cria esse estado como o [AlwaysOnTop](https://github.com/TopSoftdeveloper/AlwaysOnTop): corre elevada, copia o token do `winlogon.exe` da sessão e relança-se com `UIAccess` ativo. Por isso:

- **Pede permissão de administrador** (UAC) uma vez, ao clicar em Instalar, se escolheres esse modo no instalador.
- **Arranca por uma Tarefa Agendada** (`CursorMeter`, privilégio mais alto), registada pelo instalador. Abrir o CursorMeter corre essa tarefa e não pede o UAC. O arranque com o Windows liga e desliga o gatilho de início de sessão dessa tarefa, em vez de usar um atalho.
- **É experimental.** Se o UIAccess não ficar ativo, a app continua sem ele, como a Island, e o motivo fica no registo.

O modo ativa-se no instalador, no passo **Selecione o modo de instalação**. Se for detetado um Start11 anterior ao v3, esse passo recomenda a opção com permissão de administrador e explica porquê. Se a Mainland já estiver instalada, o passo só oferece atualizar (mantém o modo, sem UAC) ou desinstalar primeiro a versão instalada; mudar de modo obriga a desinstalar. Desinstalar apaga a Tarefa Agendada. Para o desligar sem desinstalar, apaga `experimental-admin` na pasta do programa e a tarefa `CursorMeter` no Agendador de Tarefas.
