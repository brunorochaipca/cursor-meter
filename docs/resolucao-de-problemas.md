# Resolução de problemas

## As barras não aparecem (Windows)

Abre **Acerca de** no menu do botão direito: mostra a versão e o ficheiro de registos (`%LOCALAPPDATA%\CursorMeter\cursormeter.log`), que diz como as barras foram colocadas. O botão **Enviar registos** copia o registo e abre uma issue no GitHub.

Confirma também que o Cursor tem a sessão iniciada: o CursorMeter lê a sessão que o Cursor gravou. Se essa sessão tiver expirado, usa a de cursor.com no Firefox.

## "Ligar a conta do Cursor" (Windows)

Sem sessão, o resumo mostra o que falta em cada fonte: o Cursor e o Firefox. Só o Firefox é lido, não o Edge nem o Chrome. Lê-se o Firefox do instalador da Mozilla e o da Microsoft Store. Se entraste em cursor.com no Firefox e continua sem funcionar, o Firefox pode não estar a guardar cookies: a opção "Apagar cookies e dados dos sites quando o Firefox fecha", o modo privado permanente ou uma política da organização. Abre uma exceção para `https://cursor.com` em Definições › Privacidade e segurança › Cookies › Gerir exceções, ou instala o Cursor e inicia sessão lá. Depois carrega em **Verificar já**. A app não volta a verificar sozinha enquanto o resumo está aberto.

## Onde ficam os ficheiros (Windows)

Em `%LOCALAPPDATA%\CursorMeter`:

| Ficheiro | Para quê |
|---|---|
| `settings.txt` | Definições |
| `offset.txt` | Posição das barras fora do modo ilha |
| `floating.txt` | Posição das barras flutuantes |
| `cursormeter.log` | Registo |

## O Windows diz que o publicador é desconhecido

O instalador não está assinado. Escolhe **Mais informações** e depois **Executar mesmo assim**.

## Zorin: não vejo as barras

Termina a sessão e entra outra vez depois de instalar, para o GNOME carregar a extensão. Ver [Zorin OS](zorin.md).

## Com o Start11 v1 ou v2 a barra de tarefas fica por cima das barras

Acontece depois de abrir e fechar o menu Iniciar ou a Vista de Tarefas com o rato. A edição Mainland tem um modo experimental para isto. Ver [as duas edições](edicoes.md).
