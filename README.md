# CursorMeter

Duas barras na barra de tarefas do Windows, só para o Cursor:

`37% Cursor Models | 64% Other Models`

Cada barra é a quota desse pool no ciclo atual. Cursor Models corresponde a `autoPercentUsed`. Other Models corresponde a `apiPercentUsed`. São as mesmas duas percentagens do painel Usage.

Não há cookie para colar. O cabeçalho Cookie que o TaskbarQuota pede é a sessão do browser em cursor.com; o Chrome e o Edge recentes cifram esses cookies, por isso a deteção automática falha. Esta aplicação não usa isso. Lê a sessão que o próprio Cursor gravou ao iniciares sessão, em `%APPDATA%\Cursor\User\globalStorage\state.vscdb`.

## Correr

No Windows, com o [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0):

```powershell
.\scripts\build-windows.ps1
.\src\CursorMeter.App\bin\Release\net8.0-windows\win-x64\publish\CursorMeter.exe
```

O executável fica ao lado do relógio, dentro da barra de tarefas. Arrasta para o reposicionar. Clique abre o resumo. Botão direito: atualizar, abrir o painel, iniciar com o Windows, sair.

A posição fica em `%LOCALAPPDATA%\CursorMeter\offset.txt`.

## O que o .NET precisa de ter

- SDK .NET 8, para compilar.
- No Windows, o workload de ambiente de trabalho (`net8.0-windows` / Windows Forms), que vem com o SDK instalado no Windows.
- Não é preciso o Windows App SDK nem o WinUI do TaskbarQuota.

A lógica (sessão, pedido, texto das percentagens, posição na barra) corre em qualquer sistema com o SDK. A janela da barra de tarefas só existe no Windows.
