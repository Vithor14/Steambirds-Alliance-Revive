# Multiplayer, contas por jogador e launcher 0.3.0

Servidor **v11** e launcher **0.3.0** (16/11/2026, testados em 22/11 com dois PCs), revisado em 23/11/2026 para o launcher **0.5.0**. O que existe: vários PCs entram no mesmo servidor, cada jogador com a própria conta, e os jogadores se veem no Nexus, no mundo aberto e nas dungeons (`PlayerRelay`; o tutorial segue privado). O que não existe: troca de servidor entre hosts diferentes, limite de jogadores por dungeon e fila. As partes de amigos e flocks estão em `docs/social.md`; o mundo aberto, em `docs/open-world.md`.

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

## Contas (`Server\Accounts.cs`)

- Uma pasta por jogador: `accounts\<chave>\account.json` (com `Key`, `Name`, `SteamId`, `CreatedAt`, `LastAccess`), `inventory.json`, `acceptances.json` e, conforme o uso, `store.json` (compras, `docs/stores.md`) e `social.json` (amigos, `docs/social.md`). A pasta `accounts\` fica fora do git.
- A conta única antiga (`account.json`, `inventory.json`, `acceptances.json` na raiz do servidor) **não é mais migrada** desde 22/11 (a camada de compatibilidade foi removida).
- **Escopo de conta**: o resto do servidor chama `Accounts.X()` / `InventoryStore.Read/Change` sem dizer a conta. A conta é a do escopo atual (`AsyncLocal`):
  - `Program.Udp.cs` abre o escopo com a conexão de cada mensagem; o welcome e a desconexão também;
  - as trocas de servidor (`Program.Transfers.cs`), `PilotProgression.EnemyKilled/PlaneDestroyed`, `LootDrops.EnemyKilled`, `InventoryHandler.PlaneDestroyed`, `DungeonServer.LeaveToNexus/MarkFinished` e o tutorial abrem o próprio (são chamados também pelos laços de atualização).
  - Sem escopo: a conta do único jogador online; com nenhum ou vários, uma conta temporária que não salva, e o log avisa uma vez qual método ficou sem escopo (`[ACCOUNT] WARNING: ...`). Esse aviso no log é o sinal de um caminho que precisa de escopo.
- `PilotProgression.Start` agora só carrega dados; o que era por conta (patente inicial de piloto novo, níveis, desbloqueios) virou `PilotProgression.PrepareAccount()`, chamado uma vez por conta no login. O inventário de cada conta é lido no primeiro uso (`InventoryStore.Current`), já com os inventários dos aviões e o estado inicial.

## Rede (`network.json`, `Network\ServerNetworkSettings.cs`)

| Chave | Padrão | Observação |
|---|---|---|
| `serverName` | Steambirds server | lista dos launchers e `procname` |
| `allowOtherPlayers` | true | HTTP em `http://*:<porta>`; false = `localhost` e o UDP recusa outros PCs. Muda ao reiniciar |
| `showInServerSearch` | true | responde à busca dos launchers de outros PCs |
| `maxPlayers` | 16 | conferido no `ServerSetSessionTicket` do Nexus (contas distintas online); quem passa leva `Disconnect("Server full")` |
| `chatCommands` | everyone | `everyone`, `host` (só o jogo do PC do host) ou `none`; `/leave` e `/players` sempre |
| `welcomeMessage` | "" | chat extra no Nexus |
| `passwordLogin` | false | contas com usuário e senha (24/11, não testado no jogo): jogador novo da Steam recebe `user_missing` e o jogo abre a tela de registro (`/user/create`); `/user/login` com usuário e senha (sem ticket) entra na conta daquele usuário; `/user/change` troca a senha; `/passwd NOME SENHA` (host) redefine. Hash PBKDF2 com salt em `accounts\<chave>\login.json` (`Network\AccountLogins.cs`). Contas que já existem continuam entrando pela Steam |
| `httpPort` / `nexusPort` / `tutorialPort` / `dungeonPort` / `worldPort` / `searchPort` | 6443 / 1111 / 1112 / 1113 / 1114 / 6444 | lidas ao iniciar; portas UDP repetidas voltam ao padrão |

- Endereços: o servidor guarda os próprios endpoints como `127.0.0.1:<porta>`; `PlayerSessions.AddressFor(conexão, destino)` troca pelo endereço da sessão do jogador nas trocas de servidor (`Program.Transfers.cs`) e nos portais (`WorldObjects.cs`).
- Chat: "X entrou/saiu do servidor" para os outros do Nexus; `/players` lista quem está online. Textos em português ou inglês conforme o idioma do jogo de cada um.

## Busca de servidores

- Pergunta UDP para a porta 6444: `SBREVIVE?1 <nonce>`; resposta `SBREVIVE!1 {json}` com os mesmos campos do `GET /revive/info` (`game`, `name`, `build`, `protocol`, `players`, `maxPlayers`, `playerNames`, `httpPort`, `nexusPort`, `openToOtherPcs`, `acceptsPlayerName`, `accountsPerPlayer`) mais o `nonce` (`Network\LanDiscovery.cs`).
- O launcher (`Core\ServerBrowser.cs`, `Core\LocalNetworks.cs`) abre um socket por adaptador IPv4 e manda: broadcast da rede, 255.255.255.255, cada IP da tabela ARP (`GetIpNetTable`) e, em redes ≤ /22 que não são VPN, cada endereço. Radmin (26.x/8) e Hamachi (25.x/8) vão por broadcast + ARP. IPs digitados antes também são perguntados direto.
- Conectar por IP: `GET http://<ip>:<porta>/revive/info` (5 s); servidor antigo cai no `/ozy/alive`.
- `GET /revive/info` (`infoVersion` 2) também traz `playerDetails`: uma entrada por jogador online com `name`, `plane` (id da classe), `planeLevel`, `pilotLevel`, `place` (`nexus`, `tutorial`, `world` ou `dungeon`) e `where` (nome do lugar), montada em `Network\PlayerStatus.cs`. É o que o launcher usa no Discord Rich Presence. A resposta UDP da busca **não** leva `playerDetails` (tem que caber num datagrama). Nenhum endereço, chave de conta ou Steam ID sai nesses dados.

## Launcher 0.3.0 (e o que mudou até a 0.5.0)

O visual do launcher foi refeito na 0.5.0 (cabeçalho, abas agrupadas Client / Server | Map editor / Enemy AI | Settings, sub-abas); o que segue descreve o que cada parte faz, e o `Launcher\README.md` descreve as telas atuais.

- **Client**: nome do jogador (vazio = nome da Steam, lido do `loginusers.vdf` e do registro `ActiveProcess\ActiveUser`), lista de servidores (este PC + achados + digitados), Conectar por IP, *Remove from list*, busca sozinha a cada minuto. Com outro PC escolhido, "Start the server of this PC with the game" fica desmarcado e acinzentado (a preferência salva volta quando "este PC" é escolhido).
- **Download the game with Steam** (`UI\DepotDownloadForm.cs`, `Core\DepotDownload.cs`): copia o `download_depot`, abre `steam://open/console`, acompanha `Steam\logs\console_log.txt` (`Depot download complete` / `failed`) e o tamanho da pasta `depot_386011`; no fim usa a pasta e cria `steam_appid.txt` (386010). A Steam não aceita comandos de console de fora: o jogador cola e aperta Enter.
- **Server → Network and multiplayer** (`UI\NetworkPage.cs`): endereços para passar (um por adaptador, VPNs em negrito), regra de firewall `Steambirds Alliance Revive server` para o `SteambirdsServer.exe` em todos os perfis (`netsh` com UAC, `Core\FirewallRule.cs`) e o `network.json` na tela genérica de configurações.
- **Server → Players → Accounts**: lista de contas (uma por jogador), aviões da selecionada, editar (duplo clique) e remover (apaga a pasta da conta, com cópia em `_backup`); **New accounts** define o estado inicial de contas novas.
- **Settings → Server**: porta da busca (6444).
- `Build launcher.ps1`: a pasta `accounts` nunca é copiada para o dist (o dist guarda as próprias contas).

## Relay de multiplayer (17/11/2026; testado com dois PCs em 19/11 e 22/11)

`Server\Network\PlayerRelay.cs` repassa o que cada jogador faz para os outros da mesma "sala": todos do Nexus formam uma sala e cada instância de dungeon é outra (o tutorial continua privado):

- **Avião:** SpawnUnitsBatch com `playerAuthority` = PeerId do dono (o cliente só trata como avião local quando a autoridade é o próprio PeerId) e um shortId de relay (7.000.000+), igual para todos os observadores e novo a cada avião (o cliente descarta spawn de shortId vivo; também mantém os ids de bala únicos). Inclui velocidade (0, 0).
- **Movimento:** `UpdateUnitsBatch` com posição, velocidade, rotação, mira e vida, com o tempo do servidor no fim.
- **Tiros:** `GunFire` `62326` reescrito com o shortId de relay; alvo que é avião de jogador é zerado.
- **Saída:** `KillUnitsBatch` na morte, na troca de avião (`ChangePlane`) e na desconexão.
- **Chat:** `ServerProximityChat` `11732` vira `ClientProximityChat` `1612` para os outros (o cliente do remetente já mostra a própria fala).
- **Dados do jogador:** `ServerRequestRemotePlayerData` de outra conta online recebe `ClientReceiveRemotePlayerData` com o shortId de relay.

### Ajustes depois do primeiro teste com dois PCs (19/11/2026; testados em 22/11)

O teste mostrou que aviões, animação de tiro, inimigos, portões e o reset da dungeon funcionavam, mas faltavam o nome, as balas e as skills em área dos outros, e a skill em área do Old Faithful não fazia nada:

- **Nome sobre o avião:** o cliente monta o rótulo (`RemotePlayerData.GetLabelWithFlair`) a partir do campo *name* (`1024`) do `FieldUpdate` do avião, não só do `ClientReceiveRemotePlayerData`. O relay agora manda o nome da conta no spawn e em cada atualização que o dono mande com o campo. O pedido de dados de outra conta já funcionava. A rajada de pedidos a cada 30 s é o cache do cliente expirando (ele repergunta a cada frame até chegar a resposta); no teste de 23/11 isso virou ~200 respostas por segundo (28 mil linhas no relatório). Desde então o servidor responde no máximo uma vez por segundo a cada par (jogador, conta) (`Game.Rpc.cs`, `OtherDataDue`) e ignora em silêncio o pedido com `accountId` vazio.
- **Troca de servidor (23/11):** se o cliente não se desconectar sozinho em 6 s depois de `ClientSwitchToServer*` (pacote perdido em ligação ruim), o servidor fecha a conexão antiga (`Program.Transfers.cs`, `DisconnectIfStillHere`). Para jogadores de outros PCs, se em 20 s a conta não aparece no servidor de destino, o console mostra `WARNING: ... did not arrive` com a porta UDP (`WatchArrival`): quase sempre é a porta que não foi liberada no roteador.
- **Amigos no radar (23/11):** a lista de amigos agora vai ao cliente ao entrar em cada servidor (`SocialHandler.PlayerIdentified`); antes só ia quando a janela de amigos abria, e o radar (setas e cores dos amigos) lê essa lista.
- **Balas:** o `GunFire` leva `Time.fixedTime` do atirador (relógio local), e o cliente que observa só cria a bala quando o relógio do servidor chega nesse tempo. `ServerReceivePlayerInput` traz `clientFixedTime` e `clientChosenServerTime`; a diferença é o deslocamento do relógio do jogador (`PlayerRelay.ClockSample`) e o relay soma isso ao tempo do tiro antes de repassar. Sem amostra, usa o relógio do servidor.
- **Estado do avião:** o relay também repassa estado (`128`), flags (`2048`), sinal das armas (`4096`, flash e recuo) e os dois efeitos (`8192`, `16384`).
- **Nuvens para todos:** `Game.CreateCloud` manda o spawn da nuvem ao dono e aos outros jogadores da sala que veem o avião dele (`PlayerRelay.Observers`); dentro das opções da nuvem o dono vai com o id que o receptor conhece (próprio shortId ou id de relay). Quem entra depois da nuvem criada não a vê (dura segundos).
- **Veneno (Old Faithful):** a nuvem de veneno só causa dano no cliente que é autoridade do alvo, e os inimigos são do servidor; `Skills\CloudDamage.cs` aplica `effect` de vida por segundo (configBinary `clouds`) aos inimigos dentro do raio, via `Enemies.DamageArea`. O multiplicador de dano do avião do original não é conhecido (`CloudDamage.DamageMultiplier = 1`).

Repassado em 22/11: `TriggerOtherPlayerSkill` `65043` (`Game.SkillRelay.cs`: o gatilho vai para o dono da nuvem como `TriggerPlayerSkill` `65038`) e o fim das balas (`PlayerRelay.BulletHit` → `ClientKillBullet` `8267`: a bala de um jogador termina onde acertou na tela dos outros, com o id de relay do dono no `BulletId`). Ainda não repassa: `GunFireBatch` `62327`, `61257`/`26781` (o estado vem pelo `UpdateUnitsBatch`). O loot já é por jogador (cada membro sorteia o próprio).

## Dungeons compartilhadas (17/11/2026; testado com dois PCs em 22/11)

- **Entrada:** `DungeonServer.PrepareSession` procura uma instância em andamento do mesmo tipo (não terminada) e o jogador entra nela; senão cria uma nova. `Session.Members` tem os jogadores; `sessions` guarda a mesma instância uma vez por jogador.
- **Estado:** quem chega depois recebe os inimigos, peças de portão e o contador de chefes que já existem (`Enemies.SendStateTo`), mais o chão do mapa como sempre.
- **Inimigos:** a instância de `Enemies` também tem vários jogadores (`Instance.Members`); spawns, updates, tiros, mortes e falas vão para todos. Cada inimigo persegue e mira o jogador vivo mais próximo; tiros teleguiados usam o id que cada cliente conhece (o próprio avião ou o id de relay do outro).
- **Progresso:** salas, portões, ondas e checkpoint são da instância (qualquer jogador vivo perto acorda uma sala). XP e loot de cada abate vão para todos os jogadores da instância, cada um com os próprios sacos.
- **Comandos:** `/leave` tira só quem digitou; `/room` e `/boss` teleportam só quem digitou; `/restart` recomeça a dungeon para todos.
- **Visão de inimigos:** no mundo aberto cada cliente só recebe os inimigos dentro de `enemies.viewRadius` (23/11, `Enemies.Interest.cs`); nas dungeons e no tutorial o servidor continua mandando tudo.
- **Reset:** quando o último jogador sai (ou desconecta) a instância e o estado dos inimigos são descartados; a próxima entrada começa do zero. Uma dungeon já concluída não aceita novos jogadores.
- **Ainda não:** limite de jogadores por dungeon (o `maxPlayers` do `dungeons.json` ainda não barra a entrada), fila, escalar a dificuldade pelo número de jogadores.
