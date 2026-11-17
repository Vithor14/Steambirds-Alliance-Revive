# Steambirds Alliance Revive — Launcher

Launcher do projeto, com quatro abas (versão em inglês: `README.md`):

- **Client**: baixa o jogo pela Steam, escolhe o nome do jogador, lista os servidores (este PC, rede local e VPNs como Radmin e Hamachi, ou um IP digitado) e abre o jogo no servidor escolhido. Tem um console que mostra o `BepInEx\LogOutput.log` e o `Player.log` do Unity enquanto o jogo roda.
- **Server**: sobe o servidor deste PC (o que o `Run server.ps1` fazia) para este PC e para jogadores de outros PCs, mostra a saída dele num console e edita as configurações, a rede (nome, portas, quem entra) e as contas dos jogadores.
- **Settings**: tema, idioma do launcher, pasta e idioma do jogo, e os botões para abrir pastas e arquivos (jogo, BepInEx, servidor, mapas, logs, backups, configurações).
- **Map editor**: cria, abre, edita e salva os arquivos de mapa do servidor (dungeons, Nexus, tutorial) com as peças do jogo. Veja `Editor\README.md`.

O launcher abre em **inglês**. Para usar em português: *Settings → Appearance → Launcher language → Português (Brasil)*.

O que ainda não existe no projeto aparece **acinzentado**, com a marca *(coming soon)* / *(em breve)*. Passe o mouse em cima dela para ver o motivo.

## Como montar

Na pasta do projeto (`C:\Steambirds`), rode **`Build launcher.ps1`** (botão direito → *Executar com o PowerShell*). Ele:

1. procura arquivos `.cs` duplicados (a mesma regra do `Run server.ps1`, e também o mesmo arquivo em duas pastas);
2. compila o servidor (`Server`), o plugin (`QuieterPlugin`) e o launcher como executáveis *self-contained* (quem recebe a pasta não precisa instalar o .NET);
3. monta a pasta **`Launcher\dist`**, com os idiomas de `Launcher\lang` em `dist\lang` e as imagens do editor de mapas (`Launcher\editor-assets`, geradas pelo `tools\editor\build_editor_assets.py` a partir dos assets extraídos do jogo; ficam fora do git) em `dist\editor-assets`.

Depois disso, basta abrir `Launcher\dist\SteambirdsLauncher.exe`. Rode o script de novo sempre que o código do servidor, do plugin ou do launcher mudar. O log da compilação fica em `Launcher\build\compilacao.log`.

## O que fica na pasta do launcher

Tudo é salvo na pasta onde está o `SteambirdsLauncher.exe`:

| Caminho | O que é |
|---|---|
| `launcher.json` | configurações do launcher (pasta do jogo, nome do jogador, servidor escolhido, IPs digitados, porta da busca, tema, idiomas, opções, tamanho da janela) |
| `lang\` | idiomas do launcher (`pt-BR.json`, `_template.json` para criar outros, `README.md`) |
| `editor-assets\` | imagens e listas de nomes que o editor de mapas desenha (opcional: sem elas ele desenha formas) |
| `logs\servidor-<data>.log` | tudo o que o servidor escreveu em cada sessão (guarda as 30 últimas) |
| `logs\cliente-<data>.log` | o que o console do cliente mostrou em cada sessão do launcher |
| `logs\server-report.txt` | o mesmo relatório do `Run server.ps1`: saída do servidor, logs do jogo, as contas (`account.json`) e os `.txt` do BepInEx |
| `logs\launcher-errors.txt` | erros inesperados do launcher |
| `server\` | o servidor e os dados dele: `mapas`, `dados`, `loot_tables`, `itens` e as configurações (`progression.json`, `items.json`, `dungeons.json`, `enemies.json`, `network.json`) |
| `server\accounts\<chave>\` | uma conta por jogador (`steam_<SteamID>`): `account.json`, `inventory.json`, `acceptances.json` |
| `server\_backup\` | cópias feitas antes de trocar arquivos: pelo script (`<data>`), ao salvar uma conta (`conta-<data>`) e ao remover uma conta (`account-removed-<data>`) |
| `plugin\QuieterPlugin.dll` | o plugin, que o botão *Install plugin* copia para `BepInEx\plugins` do jogo |

As contas do launcher são separadas das do `Run server.ps1`. A conta antiga (`account.json` na raiz do servidor, de antes das contas por jogador) vai para o **primeiro jogador que entrar pelo próprio PC do servidor**, com cópia em `server\_backup\migracao-<data>`. Na primeira compilação o script copia a conta antiga do `Server`; a pasta `contas` nunca é copiada.

### Atualizar os dados com o script

Ao rodar o `Build launcher.ps1` de novo, mapas, tabelas de loot e configurações do `Server` só substituem os do launcher quando são **mais novos**. Antes de substituir, o arquivo do launcher vai para `server\_backup\<data>`. Assim, uma configuração mudada pelo launcher não se perde, e uma versão nova de um mapa ou de um arquivo de configuração chega ao launcher. Os idiomas seguem a mesma regra: um arquivo de `dist\lang` editado depois do de `Launcher\lang` é mantido, com um aviso.

Para publicar o launcher para outras pessoas, apague antes as contas e os logs da pasta `dist` (`server\contas`, `server\account.json`, `server\acceptances.json`, `server\inventory.json`, `server\client-uploads`, `server\_backup`, `logs`, `launcher.json`, `lang\*.missing.json`).

## Aba Client

- **Game**: pasta do jogo (o botão *Change...* leva à aba Settings), a build do jogo (o servidor foi feito para a **201219.1413**) e o idioma escolhido para o jogo.
- **Download the game with Steam**: abre uma janela que copia o comando `download_depot 386010 386011 58813776496064724`, abre o console da Steam (`steam://open/console`) e acompanha o download (tamanho da pasta e as linhas `Depot download complete` / `failed` do `logs\console_log.txt` da Steam). No fim, a pasta `steamapps\content\app_386010\depot_386011` vira a pasta do jogo e ganha o `steam_appid.txt` (386010), que deixa o jogo entrar com a Steam fora da biblioteca. A conta precisa ter o jogo na biblioteca. O depot não traz o BepInEx: sem ele o jogo roda sem o plugin Quieter.
- **Steam**: o jogo entra com a conta da Steam, então ela precisa estar aberta.
- **Quieter plugin**: mostra se o plugin está instalado no jogo e se é a mesma versão do launcher. O jogo e o servidor rodam sem ele, mas algumas correções estão nele (arte do Flock Hall, números da tela de morte, peças de retrato do piloto).
- **Player**: o nome que os outros jogadores veem. Vazio = o nome da conta Steam (lido do `loginusers.vdf` da Steam). A **conta** no servidor é sempre a conta Steam (o Steam ID sai do ticket de login), então trocar o nome não troca o piloto nem os aviões. O nome vai no caminho do `OZY_URL` (`http://<ip>:<porta>/n/<nome em hex>/ozy`), só para servidores que aceitam (os da versão nova).
- **Servers**: a primeira linha é **este PC**; abaixo, os servidores achados na rede local e nas VPNs, e os IPs digitados.
  - A busca (*Search again*, ou sozinha a cada minuto com *Search by itself*) manda um pacote UDP para a porta 6444 em cada rede do PC: broadcast, os PCs da tabela ARP e, em redes pequenas (casa), cada endereço. Radmin VPN e Hamachi entram pelo broadcast e pela tabela ARP.
  - *Connect by IP*: digite o IP do PC do servidor (e `:porta` se o login não for 6443). O launcher confere em `http://<ip>:<porta>/revive/info` e põe o servidor na lista (os IPs digitados ficam salvos).
  - Clicar numa linha escolhe o servidor (linha em negrito); duplo clique já abre o jogo. *Remove from list* tira um IP digitado.
- **PLAY**: abre o jogo no servidor escolhido. Em **este PC**: `OZY_URL=http://localhost:<porta>/ozy` e `-connecttolocal` (o mesmo de antes); com *Start the server of this PC with the game* marcado, o servidor sobe antes. Em **outro PC**: `OZY_URL=http://<ip>:<porta>/ozy`, sem `-connecttolocal` — o login devolve o Nexus daquele servidor. Com outro PC escolhido, *Start the server of this PC with the game* fica desmarcado e acinzentado.

## Aba Server

- **Start / Stop / Restart** e a situação do servidor (tempo rodando e conexões abertas), com o nome, as portas e se ele aceita outros PCs.
- **Console**: a saída do servidor, com filtro e cores. Quando o servidor para, o launcher grava o `logs\server-report.txt` (dá para desligar na aba Settings). A linha de comando está acinzentada: os comandos de admin continuam no chat do jogo.
- **Progression**, **Items and loot**, **Enemies and difficulty**: as telas são montadas a partir dos próprios arquivos do servidor. Cada chave vira um campo; a explicação vem do launcher (em inglês) ou, em português, do comentário acima da chave no arquivo. Salvar troca só os valores mudados e mantém os comentários. O servidor lê esses arquivos de novo sozinho, também com ele rodando.
- **Accounts**:
  - *Starting state of new accounts*: patente inicial, **desbloqueios iniciais em checklist** (as classes de avião do jogo, com a classe e o item de desbloqueio, mais um campo para outros itens de desbloqueio), **um campo por moeda** (as moedas do jogo) e os ingredientes e itens iniciais no armazém no formato de antes. Vale para cada jogador novo.
  - A **lista de contas**: uma por jogador (nome, Steam ID, último jogo, patente, aviões, abates...), e embaixo os aviões da conta selecionada. A conta antiga do servidor aparece no topo até ser entregue ao host. **Edit account...** (ou duplo clique) e **Remove account** só funcionam com o servidor do launcher parado, porque o servidor guarda na memória as contas de quem entrou e gravaria por cima das mudanças.
  - *Edit account...* abre uma janela com as abas Pilot (patente, XP base, abates, chefes, aviões perdidos), Planes (variante, nível, XP e XP guardado de cada avião; remover avião), Unlocks (checklist), Dungeons (dungeons terminadas, incluindo o tutorial) e Currencies and ingredients. Antes de salvar, a conta vai para `server\_backup\conta-<data>`.
  - *Remove account* move a conta para `server\_backup\account-removed-<data>`; o jogador ganha uma conta nova na próxima entrada.
- **Maps and dungeons**: para cada dungeon, se está ativa, o arquivo de mapa (qualquer `.json` de `server\mapas`), se o porta-aviões aparece no Nexus, o multiplicador de vida e quando ela abre (`aaaa-mm-dd hh:mm`). Os mapas do Nexus e do tutorial e a prévia dos mapas estão acinzentados.
- **Network and multiplayer**: os **endereços para passar** aos outros jogadores (um por rede: rede local, Radmin VPN, Hamachi...) com botão de copiar, a regra do **Firewall do Windows** (*Allow in the Windows Firewall* cria uma regra de entrada para o `server\SteambirdsServer.exe` em todas as redes; o Windows pede administrador) e as configurações do `network.json`: nome do servidor, se aceita jogadores de outros PCs, se aparece na busca, máximo de jogadores, quem usa comandos de chat (`everyone`, `host`, `none`), mensagem de boas-vindas e as **portas** (login TCP, Nexus, tutorial, dungeon e busca UDP). Portas e "outros PCs" mudam ao reiniciar o servidor; o resto o servidor relê sozinho.
- **World map**: aba acinzentada.

## Aba Map editor

Desenha o mapa do servidor (`server\maps\*.json`) com as peças de chão e as unidades do jogo. Cria um mapa a partir de uma dungeon do jogo ou de uma dungeon vazia, abre, salva (o nome do arquivo é o id do mapa), desfaz e refaz, e confere o mapa antes de salvar, cobrando os objetos obrigatórios (chão, entrada, ponto de saída, algo que termine a dungeon). Um mapa com erros só é salvo como rascunho (`"rascunho": true`). Tudo o que o servidor lê de um mapa de dungeon tem lugar nela: chão e muros, entrada, saída, pontos de controle, portões, salas com inimigos e ondas, tipos de unidade com fases e invocações, pontos, áreas, linhas, rotas, placas, decoração e prédios. O editor de **IA dos inimigos** está acinzentado por enquanto; o espaço (`"ia"` em cada tipo de unidade) já é preservado. Detalhes, atalhos e mapa do código: `Editor\README.md`.

## Aba Settings

- **Appearance**: tema claro ou escuro (o console é sempre escuro), idioma do launcher e a pasta de idiomas. Trocar o idioma remonta as abas na hora; o servidor e os consoles continuam rodando.
- **Game**: pasta do jogo (*Browse...* / *Detect*, pela instalação da Steam em `steamapps\content\app_386010\depot_386011`) e **idioma do jogo**. A lista vem do próprio jogo (`Texts/locales` do configBinary). Com um idioma escolhido, o launcher abre o jogo com `-locale <id> -skipInitMenu`; *Chosen in the game* deixa o jogo usar o idioma do menu de opções dele.
- **Server**: gerar o relatório quando o servidor parar.
- **Multiplayer**: a porta UDP em que a aba Client procura servidores (6444; precisa ser a `searchPort` dos servidores).
- **Open folders and files**: pastas (jogo, BepInEx, launcher, servidor, mapas, contas, tabelas de loot, logs, backups, idiomas), logs (relatório, último log do servidor, `LogOutput.log`, `Player.log`) e arquivos do servidor (`progression.json`, `items.json`, `enemies.json`, `dungeons.json`, `network.json`, `launcher.json`).

## Jogar com outras pessoas

1. **Quem hospeda** abre o launcher, aba *Server → Network and multiplayer*: confere que aceita outros PCs, clica em *Allow in the Windows Firewall* (uma vez) e inicia o servidor. Na mesma aba estão os endereços para passar (o do Radmin/Hamachi, ou o da rede local).
2. **Quem entra** abre o launcher, aba *Client*: o servidor aparece na lista (mesma rede ou VPN) ou é adicionado com *Connect by IP*. Escolhe a linha e clica em **PLAY**.
3. Pela internet (sem VPN), o roteador de quem hospeda precisa encaminhar para o PC dele a porta de login (TCP 6443) e as portas do jogo (UDP 1111, 1112 e 1113), e quem entra digita o IP público.

Cada jogador tem a própria conta no PC de quem hospeda (piloto, aviões, itens, dungeons terminadas). Por enquanto **cada jogador joga no próprio mundo**: os inimigos, os baús e o loot são de cada um, e os jogadores ainda não se veem (repassar os aviões, tiros e skills dos outros é a próxima etapa do multiplayer). O chat avisa quem entra e sai, e `/players` lista quem está online.

## Idiomas

O texto do código é o inglês, e ele mesmo é a chave das traduções. Cada idioma é um arquivo `lang\<código>.json` (`pt-BR.json` vem com o projeto). Para criar outro, copie `lang\_template.json`, preencha `_language` e traduza os valores; o passo a passo está em `lang\README.md`. Textos sem tradução aparecem em inglês e o launcher lista os que faltaram em `lang\<código>.missing.json` quando fecha.

Ao escrever código novo no launcher, todo texto que aparece na tela passa por `L.T("...")` (ou `L.F("... {0}", valor)`), e a tradução entra em `Launcher\lang\pt-BR.json` e em `_template.json`.

## Código

O código fica em `Launcher\` (WinForms, .NET 8, sem arquivos de designer):

- `Core\`: o que não é tela. Processo do servidor e do jogo, leitura dos logs, relatório, edição dos `.json` com comentários, `dungeons.json`, `network.json` (`ServerNetworkFile`), as contas (`AccountStore`, `AccountFile`), a busca de servidores (`ServerBrowser`, `LocalNetworks`), a conta da Steam (`SteamAccount`), o download do depot (`DepotDownload`), a regra do firewall (`FirewallRule`), idiomas (`Lang`) e o buffer dos consoles.
- `Core\GameData\`: leitura do configBinary do jogo (classes, desbloqueios, moedas, ingredientes, tabelas de XP e idiomas).
- `Editor\`: a aba do editor de mapas (o documento, o canvas, os formulários e as conferências; veja `Editor\README.md`).
- `UI\`: as telas. `MainForm`, `ClientTab`, `DepotDownloadForm`, `ServerTab`, `NetworkPage`, `SettingsTab`, `ConsoleView`, `SettingsPage`, `AccountsPage`, `AccountEditorForm`, `AccountControls`, `MapsPage`, `Theme` (tema claro/escuro) e `LauncherContext` (estado compartilhado entre as abas, inclusive a lista de servidores).

Quando uma função nova entrar no servidor, a parte acinzentada correspondente no launcher é ligada: procure por `Ui.ComingSoon`, `Ui.Disable` e `MarkNotImplemented`.
