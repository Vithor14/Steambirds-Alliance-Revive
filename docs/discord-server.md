# Servidor do Discord — framework

> Proposta de **22/11/2026**. Nada foi criado no Discord ainda e o repositório não tinha nenhuma menção a ele.
> Este arquivo é um plano para montar e operar o servidor da comunidade do Steambirds Alliance Revive.
> **Decisão do Vithor (22/11/2026): o servidor é separado em português e inglês.** O desenho abaixo parte disso.
> As decisões que ainda estão abertas estão na seção 16.

## 1. Para que o servidor serve (e para que não serve)

O projeto tem quatro coisas que dependem de gente: **jogar** (precisa de servidores abertos e de grupo), **hospedar**
(rede, VPN, firewall), **criar** (mapas, dungeons, IA de inimigos, loot como arquivos de dados) e **lembrar** (o que o
jogo original fazia, porque as regras ficavam no servidor que sumiu em 2021). O Discord existe para ligar essas pessoas.

**Regra central: o Discord é onde se conversa; o GitHub é onde se decide e se registra.**

| Tipo de informação | Onde ela mora | O que o Discord faz |
|---|---|---|
| Download, instalação, requisitos | `README.md` e GitHub Releases | Fixa o link; nunca copia o texto longo |
| Estado das funções (feito, parcial, falta) | `CHECKLIST.md` | Avisa quando muda; recebe pedidos |
| Bugs reproduzíveis e pedidos | GitHub Issues | Triagem na conversa, depois vira issue com link de volta |
| Notas de design | `docs\` | Discussão; o que se confirma vira `docs\` ou issue |
| Mapas e dungeons da comunidade | Arquivos `.json` | Vitrine, feedback e versões |
| Servidores abertos agora | Os próprios hosts | Lista curada por fórum |

Texto copiado fica velho. O Discord linka; quem manda é o repositório.

## 2. Quem entra e o que cada pessoa precisa

| Perfil | Quer | Precisa encontrar |
|---|---|---|
| **Jogador** | Instalar e jogar hoje | Guia de 3 passos, FAQ, lista de servidores, grupo para dungeon |
| **Host** | Abrir servidor para amigos | Ajuda de rede (portas, Radmin, Hamachi, firewall), configurações, onde divulgar o servidor |
| **Criador** | Fazer mapas, dungeons, IA, loot | Vitrine com versão e prévia, feedback, ajuda com o editor |
| **Testador** | Jogar versões novas e reportar | Chamada de teste com o que testar e onde reportar |
| **Veterano** | Passar o que lembra do jogo original | Um lugar para registrar (vídeos, chefes, economia, roteiros) |
| **Dev** | Contribuir com código | Canal técnico, feed do GitHub, `CONTRIBUTING.md` |

O **Testador** e o **Veterano** são os perfis específicos deste projeto. O teste dentro do jogo é manual e há muita
coisa marcada como "compilada, ainda não testada no jogo" no checklist. E boa parte do que falta recriar (IA original
dos chefes, roteiros das dungeons, taxas de loot) só existe na memória e nos vídeos de quem jogou.

## 3. Princípios

1. **Comece pequeno e cresça por gatilho.** Um servidor novo com dezenas de canais vazios parece morto. A seção 5 lista o
   mínimo e o que dispara cada canal novo.
2. **O idioma separa a conversa; o dado e o conhecimento raro ficam juntos.** Veja a seção 4.
3. **Fórum para o que precisa ser achado depois, chat para o que passa.** Suporte, servidores, mapas e testes são
   fóruns com tags. Conversa solta fica em `#geral` / `#general`.
4. **Tudo que se repete tem modelo** (bug, servidor, mapa, chamada de teste, release). Estão no apêndice B.
5. **Privacidade primeiro.** O `server-report.txt` traz o `account.json` com Steam IDs; hosts não devem expor o IP de casa.
6. **Nunca arquivos do jogo.** O projeto exige que cada pessoa tenha o próprio jogo na Steam (`CONTRIBUTING.md`).
7. **Poucos bots.** Primeiro as ferramentas nativas do Discord (Comunidade, Integração, AutoMod, Fóruns, Eventos).
8. **Um único lugar de download:** o GitHub Releases. Veja a seção 10.

## 4. Idiomas: o que separa e o que une

O servidor tem **uma zona em inglês, uma zona em português e uma zona global**. Cada pessoa escolhe o idioma ao entrar
(pode escolher os dois) e só vê as zonas que escolheu mais a global.

**Regra de decisão:** *se o canal é conversa ou ajuda, que depende de entender o texto, ele existe em cada idioma. Se é
dado (arquivos que não têm idioma) ou conhecimento raro (poucas pessoas sabem), ele fica na zona global.*

| Zona | O que tem | Por quê |
|---|---|---|
| **Inglês** e **Português** (iguais, uma por idioma) | Boas-vindas, anúncios, FAQ, conversa, lista de servidores, suporte, voz | Quem pede ajuda ou procura grupo precisa ser entendido e entender |
| **Global** (bilíngue) | Criações (mapas, dungeons, IA, loot), testes, memória do jogo original, prints e clipes, voz de teste | Um mapa em `.json` não tem idioma; o teste de multiplayer precisa juntar gente; quem lembra do jogo original é pouco e vale mais junto |
| **Develop** (só inglês) | Conversa técnica e feed do GitHub | Código, issues e PRs são em inglês (`CONTRIBUTING.md`) |
| **Staff** (oculta) | Moderação | — |

**Como funciona na prática**
- Os canais de cada idioma são **espelhos** um do outro, com os mesmos modelos; só mudam o nome e o texto. Um canal
  de idioma pode ser clonado para abrir um terceiro idioma (gatilho na seção 5.3).
- Quem fala os dois idiomas escolhe os dois cargos e vê tudo. Quem só fala um vê uma zona e a global.
- Nas zonas globais, cada post leva a tag de idioma (`EN`, `PT` ou `EN+PT`) e os modelos têm os rótulos nos dois idiomas
  (apêndice B). Escreva no seu idioma; ajuda de tradução é bem-vinda.
- Os anúncios saem **nos dois idiomas em até 24 h**. A fonte é a nota de release do GitHub (em inglês); a versão em português
  fica com quem a equipe indicar. Anúncio só em um idioma é aviso de que a tradução ainda vem.
- O que um moderador de um idioma decide vale para o servidor todo; a equipe cobre os dois idiomas (seção 6).
- **Risco principal:** dividir uma comunidade pequena em duas deixa as duas parecendo vazias. Por isso a zona global
  existe, os eventos e testes são juntos e há um gatilho de fusão (seção 5.3).

## 5. Estrutura de canais

### 5.1 Dia 1 (mínimo)

Nomes dos canais: inglês / português. As duas zonas têm os mesmos canais.

| Canal (EN / PT) | Tipo | Para quê | Quem escreve |
|---|---|---|---|
| `#start-here` (visível a todos) | texto, só leitura | **Escolha do idioma**, aviso de não oficial, link oficial de download e as regras nos dois idiomas (apêndice A). É o canal de regras do Discord | Equipe |
| **Zona inglês e zona português (6 canais cada)** | | | |
| `#welcome` / `#boas-vindas` | texto, só leitura | O que é o projeto, os 3 passos (seção 8), links do GitHub, Releases e README | Equipe |
| `#announcements` / `#avisos` | anúncios (seguível) | Releases, chamadas de teste, eventos, no modelo B5 | Equipe |
| `#faq` / `#perguntas-frequentes` | texto, só leitura | Respostas fixas (seção 8) | Equipe |
| `#general` / `#geral` | texto | Conversa e procura de grupo para dungeon (`#find-players` / `#procurar-grupo` nasce por gatilho) | Todos |
| `#server-list` / `#lista-de-servidores` | **fórum** | Um post por servidor aberto (modelo B2) | Hosts |
| `#support` / `#suporte` | **fórum** | Instalação, launcher, rede, bugs (modelo B1) | Todos |
| Voz: `Lobby EN` / `Lobby PT` | voz | Jogar juntos | Todos |
| **Zona global** | | | |
| `#creations` | **fórum** | Mapas, dungeons, IA, loot, presets de configuração (modelo B3) | Todos |
| `#playtest` | **fórum** | Um post por versão em teste, bilíngue (modelo B4) | Equipe abre, todos respondem |
| `#original-game` | **fórum** | Memórias, vídeos e dados do jogo original (seção 9.5) | Todos |
| `#screenshots-clips` | texto, mídia | Prints e vídeos de partidas | Todos |
| Voz: `Playtest` | voz | Sessões de teste e dungeons com a comunidade toda | Todos |
| **Develop (inglês)** | | | |
| `#dev` | texto | Conversa técnica | Todos |
| `#github-feed` | texto, webhook | Releases, issues e PRs | Webhook |
| **Staff (oculta)** | | | |
| `#mod-chat`, `#mod-log` | texto | Moderação e registro de ações | Equipe |

Total: 24 canais, mas cada pessoa vê 13: `#start-here`, a zona do seu idioma (7), a zona global (5) e, se
marcou o interesse, mais `#dev` e `#github-feed` (15). A **Integração** do Discord (perguntas ao entrar) cuida da visibilidade:
quem marcou "desenvolver" vê o Develop; quem não marcou, não vê.

### 5.2 O que o idioma muda em cada canal de suporte e lista

- `#support` / `#suporte`: a resposta é no idioma da pergunta. O bug confirmado vira issue em inglês, escrita por um
  maintainer ou moderador, com o link colado de volta na thread.
- `#server-list` / `#lista-de-servidores`: o host posta na zona do idioma em que atende. Quem aceita os dois idiomas
  pode postar nas duas, com o mesmo texto.

### 5.3 Canais novos e fusão, por gatilho

| Quando | Faça |
|---|---|
| `#general` fica cheio de pedidos de grupo | Abra `#find-players` / `#procurar-grupo` |
| `#support` passa de ~10 posts novos por semana, e metade é rede | Abra `#hosting-help` / `#ajuda-hospedagem` |
| `#creations` passa de ~15 mapas ativos | Separe em mapas e dungeons, IA e loot (zona global) |
| `#general` vira conversa sem relação com o jogo | Abra `#off-topic` |
| Pedidos de função se repetem | Abra `#suggestions` / `#sugestoes` (fórum, com tag de status ligada ao `CHECKLIST.md`) |
| Mais de uma sessão de voz por semana | Mais canais de voz e eventos fixos |
| Surge um terceiro idioma com 10+ pessoas ativas | Clone a zona de um idioma e traduza o modelo |
| Uma zona de idioma fica com menos de ~5 pessoas ativas por 60 dias | **Fusão:** junte os canais de conversa dela com a zona global e mantenha o cargo de idioma só como etiqueta |

Canal sem atividade em 60 dias é arquivado.

## 6. Cargos e permissões

Poucos cargos. A visibilidade vem dos cargos de idioma; o resto, dos canais e do nível de verificação.

| Cargo | Quem | Como entra | O que tem de especial |
|---|---|---|---|
| **Owner** | Vithor | Dono do servidor | Tudo; conta com 2FA |
| **Maintainer** | Quem tem commit no repositório | Manual | Abre posts em `#playtest`, publica anúncios, converte bug em issue |
| **Moderator** | Moderação | Manual; **no mínimo 1 a 2 pessoas por idioma e 3 no total**, em fusos diferentes | Timeout, kick, ban, `#mod-chat`; 2FA exigido |
| **English** / **Português** | Todos | Autoatribuído na Integração (pode ter os dois) | Mostra a zona do idioma |
| **Developer** | Quem quer ver o Develop | Autoatribuído na Integração | Mostra `#dev` e `#github-feed` |
| **Host** | Mantém servidor aberto | Autoatribuído; um moderador confirma o servidor | Pode postar nas listas de servidores |
| **Playtester** | Quer testar versões | Autoatribuído | Recebe menção nas chamadas de teste |
| **Veteran** | Jogou o original | Autoatribuído | Reconhecimento e menção em perguntas sobre o jogo original |
| **Contributor** | Teve PR aceito | Manual | Reconhecimento (cor e lista) |
| **Map Maker** | Publicou mapa que valida sem erros | Manual | Reconhecimento; destaque na vitrine |

- `@everyone` não menciona `@everyone` nem `@here` e não anexa arquivos fora de `#creations` e `#support` / `#suporte`.
- Anexos ficam permitidos só onde são necessários (fóruns de suporte e criações).

## 7. Regras (resumo; texto pronto nos dois idiomas em `discord-start-here.md`)

O tom é leve de propósito: regras curtas, com o motivo de cada uma, e conversa antes de punição. Pirataria e malware são a
exceção e saem na hora. Resumo:

1. Seja gente boa.
2. Sem arquivos do jogo, chaves da Steam ou cópias piratas (cada pessoa usa o próprio jogo da Steam).
3. Clima família: nada de conteúdo +18.
4. Dados privados ficam privados: nada de dados de conta (`account.json`, Steam IDs, `server-report.txt` completo) nem IP de
   casa em canal público; convites de VPN (Radmin, Hamachi) por mensagem privada.
5. Download só pelo GitHub Releases; ignore arquivos enviados por outras pessoas; a equipe nunca pede senha nem Steam Guard.
6. Sem propaganda de outros servidores nem venda de ouro, contas ou itens (divulgar o seu servidor do jogo é bem-vindo).
7. Cada zona usa o seu idioma; nos canais compartilhados vale qualquer um, com a tag de idioma.
8. Spoilers de dungeons e chefes em tag de spoiler.
9. Projeto de fãs, não oficial.
## 8. Entrada de novos membros

1. **Entra** → tela de regras do Discord (o canal de regras é o `#start-here`).
2. **Integração:** primeira pergunta **Language / Idioma** (um ou os dois), depois "O que você quer fazer?" (jogar,
   hospedar, criar, testar, desenvolver) → cargos e canais certos.
   Quem pular as perguntas continua vendo só `#start-here` e a tela de Canais e cargos do Discord, onde escolhe depois.
3. **`#welcome` / `#boas-vindas`** mostra três passos curtos e o link do README, sem copiar o texto dele:
   1. Tenha o Steambirds Alliance na biblioteca da Steam (build do cliente 201219.1413).
   2. Baixe o `.zip` mais recente em GitHub Releases e abra o `SteambirdsLauncher.exe`.
   3. Aba **Client**: escolha um servidor (*This PC*, rede ou *Connect by IP*) e clique em **PLAY**.
4. **`#faq` / `#perguntas-frequentes`** com as perguntas que mais vão aparecer:
   - O jogo não está na minha biblioteca / como baixar o depot (`download_depot 386010 386011 58813776496064724`).
   - Como abrir servidor para amigos: portas (HTTP 6443, Nexus 1111, tutorial 1112, dungeon 1113, busca 6444), firewall,
     Radmin e Hamachi.
   - Cada pessoa joga com a própria conta, salva no PC do host.
   - O que funciona e o que ainda falta (link para o `CHECKLIST.md`).
   - Como reportar bug (modelo B1).

**Medida de sucesso:** quanto tempo entre entrar e logar numa partida. Pergunte isso nas primeiras semanas.

## 9. Fluxos específicos do projeto

### 9.1 Lista de servidores (fórum por idioma)

Complementa a lista do launcher (rede local e VPNs), que só mostra servidores alcançáveis pela rede. O fórum é a lista
feita por pessoas. Um post por servidor, no modelo B2, com os campos do `GET /revive/info`: nome, build, jogadores e máximo,
forma de acesso, configurações. **Tags:** forma de acesso (Radmin, Hamachi, IP direto), tipo (padrão, mapas da
comunidade, hardcore), vagas. O host mantém o post atualizado; o fórum arquiva por inatividade. IP e senha de rede vão por
mensagem privada.

*Possível depois:* um bot que consulta o `/revive/info` dos servidores que optarem por isso e mostra o estado em tempo real.
Só entra com hosts suficientes e com consentimento (expõe o endereço do host).

### 9.2 Suporte (fórum por idioma)

- **Tags de assunto:** Install/Steam, Launcher, Hosting/Network, In-game bug, Map editor, Enemy AI editor.
- **Tags de estado:** Open, Solved, Needs GitHub issue, Fixed in `<versão>`.
- O modelo B1 espelha o "Reporting bugs" do `CONTRIBUTING.md`: build do jogo, versão do launcher, passos, esperado,
  o que aconteceu, linhas do `server-report.txt` **sem dados de conta**.
- Um maintainer ou moderador transforma o bug reproduzido em issue (em inglês), cola o link na thread e troca a tag para
  `Needs GitHub issue` e depois `Fixed in …`.
- Perguntas repetidas viram item do FAQ, nos dois idiomas.

### 9.3 Mapas, dungeons e arquivos de dados (`#creations`, zona global)

Mapas, IA e loot são arquivos de dados, então são a forma mais fácil de a comunidade contribuir sem tocar em código.
Um post por criação, no modelo B3:
- Nome, tipo, **versão do launcher/servidor** para a qual foi feito e a versão do formato (`docs\map-format.md`).
- Prévia (print do editor) e o arquivo `.json` anexado. **Nunca um zip com arquivos do jogo.**
- Resultado do validador do editor ("0 erros", avisos listados) e como instalar (`Server\maps\`).
- Atualizações viram resposta com changelog, não post novo.
- **Tags:** Dungeon, Nexus, Tutorial, Enemy AI, Loot table, Settings preset, dificuldade e o idioma (`EN`, `PT`, `EN+PT`).
- Curadoria: lista fixada de "recomendadas" e o **Mapa do mês** (seção 11).
- Segurança: os anexos são `.json`; qualquer outro tipo de arquivo é removido. O AutoMod nativo filtra texto, não o tipo
  de arquivo, então isso é regra e revisão manual (ou bot, se o volume crescer).

### 9.4 Programa de teste (`#playtest`, zona global)

O `CHECKLIST.md` tem muita coisa "compilada mas não testada no jogo". O servidor transforma isso em rotina:
1. Um maintainer abre um post por versão (modelo B4, **escrito nos dois idiomas**): versão, o que mudou, **o que testar**
   (copiado da seção de testes do checklist), como reportar e prazo.
2. Os `Playtester` são mencionados nos dois canais de anúncios.
3. Os testadores respondem na thread com o resultado (funcionou, falhou, log sem dados de conta), em qualquer idioma.
4. O maintainer atualiza o `CHECKLIST.md` (de 🟡 para `[x]`) e agradece quem testou pelo nome.
5. Bug confirmado vira issue.

Sessão ao vivo na voz `Playtest` uma vez por semana ou por versão (Evento do Discord) é o jeito mais rápido de testar
multiplayer, que é o que o projeto mais precisa validar (Nexus e dungeons com vários jogadores). Por isso o teste fica na zona
global: separar por idioma tiraria justamente a massa de gente que o teste precisa.

### 9.5 Memória do jogo original (`#original-game`, zona global)

O que o servidor original fazia e não veio no cliente (checklist, "Regras que só existiam no servidor") só se recupera com
quem jogou, e quem jogou é pouca gente. O fórum recebe vídeos, prints, anotações e lembranças, em qualquer idioma.
- **Tags por área do checklist:** Boss AI, Dungeon script, Loot/Economy, Rebel City, Quests, Social/Flocks, Combat Sim, World map.
- Link de vídeo e descrição são bem-vindos; **arquivo do jogo, nunca**. Diga a build e a fonte ("de memória", "do vídeo X").
- Um maintainer resume o que for sólido em `docs\` ou numa issue com a marca `original-data`, citando quem trouxe.

### 9.6 GitHub no Discord

Webhook nativo do Discord (sem bot) em `#github-feed`: releases, issues abertas e PRs mergeados. Sem push de commit
(ruído). A mesma release também vai para os dois canais de anúncios com o modelo B5. O `#github-feed` fica silenciado por padrão.

## 10. Moderação e segurança

**Ferramentas nativas ligadas desde o dia 1:**
- Nível de verificação médio ou alto; filtro de mídia explícita ativo; 2FA obrigatório para moderadores.
- **AutoMod:** bloqueio de convites de outros servidores, `@everyone`/`@here` de não moderadores, spam de menção, e
  lista de palavras (`crack`, `free download`, `steam key` e variantes) **nos dois idiomas** (`baixar de graça`,
  `chave steam`, `pirata`…). Alertas vão para `#mod-log`.
- Proteção contra raid: pausa de convites e limite de contas novas se necessário.

**Escada de sanções:** lembrete da regra → timeout → kick → ban. Toda ação grava motivo em `#mod-log`, em inglês ou em
português, como o moderador preferir. Apelação por mensagem privada a um moderador diferente do que aplicou. Pirataria e
executável suspeito: remove, avisa, reincidência é ban.

**Ameaça específica deste tipo de comunidade:** quem faz mod de jogo é alvo de malware ("launcher modificado", "servidor
que dá ouro infinito"). Por isso:
- O link oficial é um só: GitHub Releases, fixado em `#start-here`.
- Nenhum executável por aqui, nem de moderador.
- Aviso fixado: a equipe nunca pede senha, Steam Guard nem código por mensagem privada.
- Moderadores confirmam o host antes de dar o cargo `Host`.

## 11. Bots, automação e cadência

**Bots:** zero de terceiros no começo (webhook do GitHub, Integração e AutoMod resolvem). Quando um bot entrar, ele tem dono,
motivo, permissões mínimas e critério de remoção. Tradução automática de anúncios não entra: a versão nos dois idiomas é
escrita por pessoas, para o texto não parecer errado em nenhum dos dois. A ideia de bot de status está na seção 9.1.

**Cadência**

| Quando | O quê |
|---|---|
| Por release | Anúncio nos dois idiomas (modelo B5) e post de teste em `#playtest` |
| Semanal | Sessão ao vivo (dungeon ou teste) na voz `Playtest`, como Evento do Discord, anunciada nos dois idiomas |
| Mensal | Mapa do mês (zona global); resumo do que mudou no `CHECKLIST.md` |
| Trimestral | Revisão de saúde: canais parados, regras, equipe de moderação, gatilho de fusão (seção 5.3) |

## 12. Plano de lançamento

**Fase 0 — Preparação (1 a 2 dias).** Decidir as perguntas da seção 16; criar o servidor com Comunidade ativada (o canal de
regras é o `#start-here` e o de atualizações é o `#announcements`); ícone a partir de `Branding\icon.png` (emblema próprio do
projeto, nunca o logo do jogo) e banner a partir de `Branding\logo.png`; montar **uma** zona de idioma completa e depois
**clonar** para o outro idioma; criar a zona global, o Develop e a Staff; AutoMod, Integração e webhook do GitHub; escrever
`#start-here`, `#welcome` e `#faq` nos dois idiomas; testar a entrada inteira com uma conta de teste, uma vez em cada idioma.

**Fase 1 — Lançamento fechado (semanas 1 e 2).** Convidar 5 a 10 pessoas por idioma que já conhecem o projeto (quem já
testou). Rodar a primeira chamada de teste com os dois idiomas juntos. Corrigir os atritos que aparecerem.

**Fase 2 — Público.** Colocar o convite no `README.md` (inglês; já está no topo), no `CONTRIBUTING.md`
(onde conversar; já está), nas notas de release e na descrição do repositório no GitHub. O convite é **um só** e abre no `#start-here`.
O atalho no launcher já existe (botão "Discord" na aba Client e "Join the Discord" em Settings; a URL fica em `Launcher\Core\LauncherInfo.cs`, os rótulos em `Launcher\lang`). Não existe mais `Launcher\LEIA-ME.md`: o README do launcher é só em inglês.
Criar os modelos de issue em `.github\ISSUE_TEMPLATE` espelhando o modelo B1 (hoje a pasta `.github` não existe). Divulgar
nos lugares onde ex-jogadores do Steambirds ainda estão (comunidade do jogo na Steam, fóruns e redes de jogos).

**Fase 3 — Crescimento.** Abrir canais novos pelos gatilhos (seção 5.3), recrutar mais moderadores por idioma, rotina de
eventos, avaliar o bot de status.

## 13. Saúde do servidor (poucas métricas)

Meça por idioma, para ver cedo se uma zona está parada.
- Tempo entre entrar e jogar a primeira partida (pergunta no onboarding).
- Pessoas ativas na semana em cada zona de idioma (base do gatilho de fusão).
- Servidores listados e ativos na semana.
- Threads de suporte resolvidas em até 48 h.
- Relatórios de teste por versão e mapas publicados por mês.
- Retenção de novos membros em 7 dias. O Insights do Discord só aparece com Comunidade ativa e depende do tamanho do
  servidor; se não aparecer, conte à mão.

## 14. Riscos

| Risco | Mitigação |
|---|---|
| **Comunidade dividida ao meio parece vazia** | Zona global para dados, testes e memória; eventos juntos; quem fala os dois idiomas vê tudo; gatilho de fusão (5.3) |
| Um idioma fica sem moderação ou sem anúncio | Mínimo de 1 a 2 moderadores por idioma; anúncios nos dois idiomas em 24 h; revisão trimestral |
| Informação duplicada e desatualizada | Discord linka README, `CHECKLIST.md`, `docs\`; só o repositório manda; FAQ e anúncios saem do mesmo texto-fonte |
| Mensagens dos dois idiomas desalinhadas | Texto-fonte em inglês (release notes do GitHub); a versão em português é revisada por um segundo moderador |
| Vazamento de Steam IDs (`server-report.txt`, `account.json`) | Regra 4, aviso no modelo B1, moderadores apagam o anexo e pedem a versão cortada |
| Arquivos do jogo e pirataria | Regra 2, remoção imediata, ban em reincidência, projeto sem nenhum arquivo do jogo |
| Malware por "launcher modificado" | Seção 10: só GitHub Releases, sem executáveis |
| IP de casa do host exposto | Regra 4, acesso por VPN ou mensagem privada |
| Cansaço dos moderadores | Mínimo 3 pessoas, fusos diferentes, regras claras, rodízio |
| Questão de direitos do jogo | Aviso de projeto não oficial, nada do jogo no repositório nem no servidor; considerar um contato cordial com o titular antes de crescer (decisão do Vithor, não é aconselhamento jurídico) |

## 15. Montagem passo a passo

- [ ] Decidir as perguntas da seção 16
- [ ] Criar o servidor e ativar **Comunidade** (regras = `#start-here`, atualizações = `#announcements`)
- [ ] Ícone (`Branding\icon.png`), banner (`Branding\logo.png`) e nome
- [ ] Cargos da seção 6 e 2FA nos moderadores
- [ ] Zona de um idioma (6 canais e voz) com os fóruns e tags (9.1 e 9.2); clonar para o segundo idioma e renomear
- [ ] Zona global (`#creations`, `#playtest`, `#original-game`, `#screenshots-clips`, voz) com as tags de idioma
- [ ] Develop (`#dev`, `#github-feed`) e Staff
- [ ] Permissões: cada zona de idioma só visível ao cargo dela; global visível a quem tem um dos dois
- [ ] AutoMod nos dois idiomas, verificação, filtro de mídia e proteção contra raid (seção 10)
- [ ] Integração: pergunta de idioma primeiro, depois interesses
- [ ] `#start-here`, `#welcome`, `#faq` nos dois idiomas e modelos B1 a B5 fixados em cada fórum
- [ ] Webhook do GitHub em `#github-feed`
- [ ] Testar a entrada com uma conta nova, uma vez em cada idioma
- [ ] Lançamento fechado, depois público (seção 12)
- [ ] Convite no README (feito em 22/11), launcher (botão Discord na aba Client e em Settings, feito em 23/11), CONTRIBUTING (feito em 23/11), release notes e descrição do repositório no GitHub; criar `.github\ISSUE_TEMPLATE`

## 16. Decisões abertas (do Vithor)

Resolvida: ~~idioma principal~~ → servidor separado em português e inglês (22/11/2026).

1. **Equipe:** quem são os 2 ou 3 moderadores (pelo menos um por idioma) e quem fica como maintainer?
2. **Zona global:** confirmar que mapas, testes, memória do jogo original e prints ficam juntos (recomendado, seção 4). Se
   preferir separação total, essas quatro também ganham versão por idioma, e o teste de multiplayer perde gente.
3. **Nome e convite:** nome do servidor e convite permanente (URL personalizada exige impulsos do Discord).
4. **Direitos do jogo:** entrar em contato com o titular antes de divulgar, ou só manter o aviso de "não oficial"?
5. **Doações e impulsos:** aceitar ou não; recomendação: nunca cargo ou vantagem de jogo em troca de dinheiro.
6. **Lista de servidores:** só por fórum (recomendado agora) ou também um bot que mostra o estado ao vivo mais tarde?
7. **Link no launcher:** adicionar o atalho "Community" na próxima versão do launcher?

---

## Apêndice A — Regras (texto pronto, para o `#start-here`)

O texto vigente das regras, nos dois idiomas, e o canal `#start-here` completo (5 mensagens prontas para colar e a tela de
regras do Discord) estão em [`discord-start-here.md`](discord-start-here.md). Ele é a única cópia: se uma regra mudar,
mude lá e ajuste o resumo da seção 7.
## Apêndice B — Modelos (para fixar em cada fórum)

Os modelos B1, B2 e B5 existem em cada idioma (um na zona inglês, outro na zona português). B3 e B4 ficam na zona
global e têm os rótulos nos dois idiomas.

**B1 — Bug / suporte** (`#support`)
```
Game build:                      (for example 201219.1413)
Launcher version:
What I did:
What I expected:
What happened:
Relevant lines of server-report.txt (no account data):
Screenshots (optional):
```

**B1 — Bug / suporte** (`#suporte`)
```
Build do jogo:                   (por exemplo 201219.1413)
Versão do launcher:
O que eu fiz:
O que eu esperava:
O que aconteceu:
Linhas relevantes do server-report.txt (sem dados de conta):
Prints (opcional):
```

**B2 — Servidor aberto** (`#server-list`)
```
Server name:
Server version / game build:
How to join:                     Radmin / Hamachi / direct IP (details by DM)
Players / max:
Time zone:
Settings:                        difficulty, XP, community maps in use
Hours open:
Contact:
```

**B2 — Servidor aberto** (`#lista-de-servidores`)
```
Nome do servidor:
Versão do servidor / build do jogo:
Como entrar:                     Radmin / Hamachi / IP direto (detalhes por mensagem privada)
Jogadores / máximo:
Fuso horário:
Configurações:                   dificuldade, XP, mapas da comunidade em uso
Horários abertos:
Contato:
```

**B3 — Criação / Creation** (`#creations`, bilíngue)
```
Name / Nome:
Type / Tipo:                     dungeon / Nexus / tutorial / enemy AI / loot table / settings preset
Language tag / Idioma:           EN / PT / EN+PT
Made for launcher/server version / Feito para a versão do launcher/servidor:
Map format version / Versão do formato:
Preview image / Imagem de prévia:
File / Arquivo:                  .json only, no game files / só .json, sem arquivos do jogo
Editor validator result / Resultado do validador:  0 errors / 0 erros (list warnings / liste avisos)
How to install / Como instalar:  put it in Server\maps\ ... / coloque em Server\maps\ ...
Changelog (updates) / Mudanças (atualizações):
```

**B4 — Chamada de teste / Test call** (`#playtest`, aberta pela equipe, bilíngue)
```
Version / Versão:
What changed / O que mudou:
What to test / O que testar:     (copied from the checklist / copiado do checklist)
How to report / Como reportar:   reply here: worked / failed + log lines without account data
                                 responda aqui: funcionou / falhou + linhas do log sem dados de conta
Deadline / Prazo:
Live session / Sessão ao vivo:   (event link / link do evento)
```

**B5 — Anúncio de release** (`#announcements`)
```
Steambirds Alliance Revive <version>
What's new:
What to test:                    (link to the #playtest post)
Known issues:
Download:                        GitHub Releases link (the only official download)
How to update:                   replace the folder / keep your accounts and maps
```

**B5 — Anúncio de release** (`#avisos`)
```
Steambirds Alliance Revive <versão>
Novidades:
O que testar:                    (link do post em #playtest)
Problemas conhecidos:
Download:                        link do GitHub Releases (o único download oficial)
Como atualizar:                  troque a pasta / mantenha suas contas e mapas
```
