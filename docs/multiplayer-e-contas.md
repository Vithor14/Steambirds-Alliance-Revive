# Multiplayer, contas por jogador e launcher 0.3.0

Servidor **v11** e launcher **0.3.0** (16/11/2026, ainda não testados). O que existe: vários PCs entram no mesmo servidor, cada jogador com a própria conta. O que ainda não existe: os jogadores se verem (cada um joga no próprio mundo; inimigos, baús e loot são por conexão).

## Fluxo de login

1. O launcher abre o jogo com `OZY_URL`:
   - **este PC**: `http://localhost:<httpPort>/n/<nome em hex>/ozy` + `-connecttolocal` (e `-connecttolocalport <nexus>` se o Nexus não for 1111);
   - **outro PC**: `http://<ip>:<httpPort>/n/<nome em hex>/ozy`, **sem** `-connecttolocal`. Sem esse argumento o cliente faz `LoginAndThenJoinNexus`: usa os `procs` do login e, com um Nexus só na lista, entra nele (`GameClient.PickPreferredNexus`).
   - O prefixo `/n/<hex>` só vai quando o servidor diz `acceptsPlayerName` no `/revive/info` (servidores antigos não reconhecem o caminho).
2. `POST /ozy/user/login` chega com `{"username":null,"steam_ticket":"<hex>_386010","session_guid":"<deviceUniqueIdentifier>"}`.
   - **Conta**: o SteamID64 sai dos bytes 12–19 do ticket de sessão da Steam (bloco do GC token: uint32 20, uint64 token, uint64 SteamID, uint32 hora). Chave `steam_<SteamID64>`; sem ticket, `pc_<hash do session_guid>`; sem os dois, `ip_<ip>`. O ticket não é validado na Steam (precisaria de Web API key).
   - **Nome**: o do caminho (`Network\PlayerNamePath.cs`), senão o salvo na conta, senão `Pilot <4 últimos dígitos>`.
   - **Endereço do servidor para esse jogador**: o host do `OZY_URL` que ele usou (header `Host`): IP digitado, achado na busca ou `localhost`. Vai nos `procs` (ips) e em toda troca de servidor.
   - Resposta: `userid` = accountId da conta, `sessid` = sessão nova (hex de 16 bytes), `procname` = nome do servidor, `players_connected`/`players_max` reais.
3. UDP `ServerSetSessionTicket(sessid, versão, username)`: liga a conexão à sessão (`Network\PlayerSessions.cs`). Sessão desconhecida → último login do mesmo IP → senão conta pelo IP.
4. `/ozy/user/self` e `/queue_check` acham a sessão pelo header `X-Session`.

## Contas (`ContaEsboco.cs`)

- Uma pasta por jogador: `accounts\<chave>\account.json` (+ `Chave`, `Nome`, `SteamId`, `CriadaEm`, `UltimoAcesso`), `inventory.json`, `acceptances.json`.
- A conta antiga (`account.json`, `inventory.json`, `acceptances.json` da raiz) vai para o **primeiro jogador que entrar pelo PC do host** (loopback ou um IP do próprio PC), com cópia em `_backup\migracao-<data>`.
- **Escopo de conta**: o resto do servidor continua chamando `ContaEsboco.X()` / `InventoryStore.Read/Change` sem dizer a conta. A conta é a do escopo atual (`AsyncLocal`):
  - `Program.TratarDados` abre o escopo com a conexão de cada mensagem; o welcome e a desconexão também;
  - `MandarParaONexus`, `MandarParaDungeon`, `NascerAviao`, `TutorialEsboco.SairParaONexus`, `PilotProgression.EnemyKilled/PlaneDestroyed`, `LootDrops.EnemyKilled`, `InventoryHandler.PlaneDestroyed`, `DungeonServer.LeaveToNexus/MarkFinished` abrem o próprio (são chamados também pelos laços de atualização).
  - Sem escopo: a conta do único jogador online; com nenhum ou vários, uma conta temporária que não salva, e o log avisa uma vez qual método ficou sem escopo (`[CONTA] AVISO: ...`). Esse aviso no log é o sinal de um caminho que precisa de escopo.
- `PilotProgression.Start` agora só carrega dados; o que era por conta (patente inicial de piloto novo, níveis, desbloqueios) virou `PilotProgression.PrepareAccount()`, chamado uma vez por conta no login. O inventário de cada conta é lido no primeiro uso (`InventoryStore.Current`), já com os inventários dos aviões e o estado inicial.

## Rede (`network.json`, `Network\ServerNetworkSettings.cs`)

| Chave | Padrão | Observação |
|---|---|---|
| `serverName` | Steambirds server | lista dos launchers e `procname` |
| `allowOtherPlayers` | true | HTTP em `http://*:<porta>`; false = `localhost` e o UDP recusa outros PCs. Muda ao reiniciar |
| `showInServerSearch` | true | responde à busca dos launchers de outros PCs |
| `maxPlayers` | 16 | conferido no `ServerSetSessionTicket` do Nexus (contas distintas online); quem passa leva `Disconnect("Server full")` |
| `chatCommands` | everyone | `everyone`, `host` (só o jogo do PC do host) ou `none`; `/sair` e `/jogadores` sempre |
| `welcomeMessage` | "" | chat extra no Nexus |
| `httpPort` / `nexusPort` / `tutorialPort` / `dungeonPort` / `searchPort` | 6443 / 1111 / 1112 / 1113 / 6444 | lidas ao iniciar; portas UDP repetidas voltam ao padrão |

- Endereços: o servidor guarda os próprios endpoints como `127.0.0.1:<porta>`; `PlayerSessions.AddressFor(conexão, destino)` troca pelo endereço da sessão do jogador nas trocas de servidor (`Program`) e nos portais (`ObjetosEsboco.EnviarPortal`).
- Chat: "X entrou/saiu do servidor" para os outros do Nexus; `/jogadores` (ou `/players`) lista quem está online. Textos em português ou inglês conforme o idioma do jogo de cada um.

## Busca de servidores

- Pergunta UDP para a porta 6444: `SBREVIVE?1 <nonce>`; resposta `SBREVIVE!1 {json}` com os mesmos campos do `GET /revive/info` (`game`, `name`, `build`, `protocol`, `players`, `maxPlayers`, `playerNames`, `httpPort`, `nexusPort`, `openToOtherPcs`, `acceptsPlayerName`, `accountsPerPlayer`) mais o `nonce` (`Network\LanDiscovery.cs`).
- O launcher (`Core\ServerBrowser.cs`, `Core\LocalNetworks.cs`) abre um socket por adaptador IPv4 e manda: broadcast da rede, 255.255.255.255, cada IP da tabela ARP (`GetIpNetTable`) e, em redes ≤ /22 que não são VPN, cada endereço. Radmin (26.x/8) e Hamachi (25.x/8) vão por broadcast + ARP. IPs digitados antes também são perguntados direto.
- Conectar por IP: `GET http://<ip>:<porta>/revive/info` (5 s); servidor antigo cai no `/ozy/alive`.

## Launcher 0.3.0

- **Client**: nome do jogador (vazio = nome da Steam, lido do `loginusers.vdf` e do registro `ActiveProcess\ActiveUser`), lista de servidores (este PC + achados + digitados), Conectar por IP, *Remove from list*, busca sozinha a cada minuto. Com outro PC escolhido, "Start the server of this PC with the game" fica desmarcado e acinzentado (a preferência salva volta quando "este PC" é escolhido).
- **Download the game with Steam** (`UI\DepotDownloadForm.cs`, `Core\DepotDownload.cs`): copia o `download_depot`, abre `steam://open/console`, acompanha `Steam\logs\console_log.txt` (`Depot download complete` / `failed`) e o tamanho da pasta `depot_386011`; no fim usa a pasta e cria `steam_appid.txt` (386010). A Steam não aceita comandos de console de fora: o jogador cola e aperta Enter.
- **Server → Network and multiplayer** (`UI\NetworkPage.cs`): endereços para passar (um por adaptador, VPNs em negrito), regra de firewall `Steambirds Alliance Revive server` para o `SteambirdsServer.exe` em todos os perfis (`netsh` com UAC, `Core\FirewallRule.cs`) e o `network.json` na tela genérica de configurações.
- **Server → Accounts**: lista de contas (uma por jogador; a antiga no topo enquanto espera o host), aviões da selecionada, editar (duplo clique) e remover (apaga a pasta da conta, com cópia em `_backup`).
- **Settings → Multiplayer**: porta da busca (6444).
- `Build launcher.ps1`: a pasta `contas` nunca é copiada para o dist; a conta antiga só vai na primeira vez (quando o dist ainda não tem `contas`); copia também o `README.md` (inglês) do launcher.

## Próximo passo do multiplayer

Fazer os jogadores se verem: para cada jogador do mesmo servidor, criar nos outros clientes o avião dele (SpawnUnitsBatch com o PeerId do dono como `playerAuthority` e shortIds próprios por observador, porque hoje todo avião usa o mesmo `ShortIdPlanoDoJogador`), repassar o estado de voo (`61257`, `26781`, `45864` → `UpdateUnitsBatch`), tiros (`GunFire`/`GunFireBatch` `62327`), skills (`TriggerOtherPlayerSkill` `65043`) e `ClientReceiveRemotePlayerData`. Depois decidir inimigos e loot compartilhados (hoje `InimigosEsboco` cria inimigos por conexão).
