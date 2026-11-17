# Comandos de chat do Steambirds Alliance

Nov 15, 2026 · @Vithor Temanski Neves Castelo

O jogo tem dois grupos de comandos de chat. Os do próprio cliente o jogo resolve sozinho, sem passar pelo servidor. Qualquer outro comando com `/` vai para o servidor: os do servidor oficial se perderam, e hoje quem responde é o nosso servidor (o esboço).

Texto sem `/` vai para o chat de proximidade. Duas barras seguidas (`//`) viram uma só.

## Comandos do próprio jogo (cliente 201219)

As descrições vêm do código do chat do cliente (`ChatManager.cs`) e dos textos de ajuda que o jogo traz. Esses comandos nunca chegam ao servidor, então o nosso servidor não pode criar um comando com os mesmos nomes.

### Gerais

| Comando | O que faz | No nosso servidor |
| --- | --- | --- |
| `/help [comando]` | Lista os comandos comuns, ou explica um (`/help trade`) | Funciona |
| `/help_all` | Lista todos os comandos do cliente | Funciona |
| `/options` | Abre o menu de opções | Funciona |
| `/resetbindings` | Volta os controles ao padrão | Funciona |
| `/clearsavedlogin` | Apaga o usuário e a senha salvos | Funciona |
| `/tutorial` | Pede para jogar o tutorial | Funciona (vai para o servidor de tutorial) |
| `/ingredients` | Abre o painel de ingredientes | Abre, mas vazio |
| `/die` | Abre o menu para derrubar o próprio avião (vida a 0) | Funciona |
| `/retire` | Abre o menu para aposentar o avião (no Combat Sim vira `/die`) | Ainda não funciona: o servidor não trata a aposentadoria |

### Social

Todos dependem de multiplayer, amigos ou flocks, que ainda não existem no nosso servidor.

| Comando | O que faz |
| --- | --- |
| `/friend NOME` | Adiciona um amigo que está no mesmo servidor |
| `/unfriend NOME` | Desfaz a amizade |
| `/block NOME` / `/unblock NOME` | Bloqueia ou desbloqueia toda interação com o jogador |
| `/blocklist` | Lista os bloqueados |
| `/mute NOME` / `/unmute NOME` | Esconde ou mostra as mensagens do jogador |
| `/mutelist` | Lista os silenciados |
| `/teleport NOME` (ou `/tp`) | Vai até o jogador. Sem nome, vai até o líder do esquadrão. Se o amigo está em outro servidor, troca de servidor |
| `/whisper NOME mensagem` (`/w`, `/tell`, `/t`) | Mensagem privada |
| `/flock mensagem` (`/f`, `/g`) | Chat da flock padrão |
| `/myflock [nome]` | Lista suas flocks ou troca a padrão |
| `/trade NOME` | Pede uma troca segura. Sem nome, aceita o último pedido |
| `/trade_reject` | Recusa o último pedido de troca |

### Só para admin

No nosso servidor todo jogador entra como admin.

| Comando | O que faz | No nosso servidor |
| --- | --- | --- |
| `/sidelength N` | Muda o tamanho da área visível (padrão 11) | Funciona |
| `/spectate` | Entra no modo espectador | Ainda não funciona |
| `/follow NOME` | Segue um jogador ou unidade durante o modo espectador | Ainda não funciona |
| `/combatsim help`, `ui LOCAL`, `start MODO [período] [semente]`, `leaderboard MODO` | Abre e inicia o Combat Sim | Ainda não funciona |

### De debug

Só existem quando a versão da Steam liga os comandos de debug; na instalação atual o `/reset_tutorial` já funcionou.

| Comando | O que faz |
| --- | --- |
| `/reset_tutorial` | Marca o tutorial como não jogado: o próximo login no Nexus manda para o tutorial de novo |
| `/reset_client_event_once_keys` | Apaga as marcas de "dica já mostrada" do cliente |

## Comandos do nosso servidor

O servidor tenta, nesta ordem: progressão, dungeons, tutorial e inimigos. Comando que nenhum deles conhece só aparece no log.

### Progressão

Funcionam em qualquer servidor.

| Comando | O que faz |
| --- | --- |
| `/progressao` | Lista os comandos de progressão |
| `/piloto` | Mostra patente, XP e situação do piloto |
| `/xp N` | Dá N de XP ao avião |
| `/nivel N` | Põe o avião no nível N |
| `/patente N` | Põe o piloto na patente N |
| `/zerar_piloto` | Zera patente, XP e skills do piloto |
| `/buff ID` | Dá um buff (sem ID, lista os buffs possíveis) |
| `/buffs` | Lista os buffs |
| `/sem_buffs` | Tira todos os buffs |
| `/retrato` | Sorteia um retrato novo para o piloto |

### Dungeons

Servidor de dungeon da v8, ainda não testado no jogo.

| Comando | Onde | O que faz |
| --- | --- | --- |
| `/dungeons` | qualquer lugar | Lista as dungeons do `dungeons.json` e se estão abertas |
| `/dungeon NOME` | qualquer lugar | Vai para a dungeon (`/dungeon bomb`, `/dungeon iron`) |
| `/dungeonajuda` | qualquer lugar | Ajuda e o estado da dungeon atual |
| `/sala` | na dungeon | Lista as salas (pontos de controle) |
| `/sala N` | na dungeon | Pula para a sala N; o que ficou para trás conta como limpo |
| `/chefe` | na dungeon | Pula para a sala do chefe |
| `/recomecar` | na dungeon | Recomeça a dungeon do zero |
| `/sair` | na dungeon | Volta ao Nexus |

### Tutorial

Só valem dentro do tutorial.

| Comando | O que faz |
| --- | --- |
| `/tutorialajuda` | Ajuda e a etapa atual |
| `/etapa N` | Pula para a etapa: 1 drones, 2 anel de drones, 3 caixa, 4 portão do chefe, 5 portal |
| `/portal` | Cria o portal para o Nexus à sua frente (ou leva até ele, se já existe) |
| `/recomecar` | Recomeça o tutorial |
| `/sair` | Depois do chefe, vai ao Nexus sem entrar no porta-aviões |

### Inimigos, para testes

Funcionam em qualquer servidor.

| Comando | O que faz |
| --- | --- |
| `/inimigos` (ou `/ajudainimigos`) | Lista estes comandos e o estado do encontro |
| `/onda N` | Pula para a onda N do encontro do `inimigos.json` |
| `/proxima` | Chama a próxima onda |
| `/chefe` | Chama a onda do chefe (dentro da dungeon, `/chefe` é o comando da dungeon) |
| `/inimigo TIPO [qtd]` | Cria inimigos de um tipo do `inimigos.json` perto de você (até 20) |
| `/spawn TEMPLATE [qtd]` | Cria qualquer unidade do jogo pelo nome do template (até 20) |
| `/limpar` | Remove todos os inimigos e para as ondas |
| `/reiniciar` | Recomeça o encontro |
| `/encontro [nome]` | Lista os encontros ou começa um |
| `/tipos` | Lista os tipos do `inimigos.json` e o template de cada um |
| `/catalogo [filtro]` | Procura unidades armadas do jogo (até 15 por vez) |

## Observações

No código do cliente também aparecem textos como `/pilot_levelup/`, `/boss/` e `/countdown/`. Não são comandos para digitar: são modelos de mensagem que o servidor manda e o cliente transforma em texto na tela.
