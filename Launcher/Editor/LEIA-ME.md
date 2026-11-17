# Editor de mapas (`Launcher\Editor`)

A aba **Map editor** do launcher cria, abre, edita e salva os arquivos de mapa que o servidor usa (`server\maps\*.json`, formato em `Server\maps\README.md`). Ela desenha o mapa com as peças do jogo, então o que você vê é o que o servidor vai montar.

Versão em inglês: `README.md`.

## Como usar

- **File → New map...** começa de uma dungeon do jogo (cópia do mapa dela; o arquivo original não é alterado) ou de uma **dungeon vazia**. **Open...** abre qualquer mapa `.json`; **Open a map of the server** lista `server\mapas`. **Save** / **Save as...** gravam o arquivo (o nome do arquivo vira o id do mapa: letras minúsculas, dígitos e `_`). `Ctrl+N`, `Ctrl+O`, `Ctrl+S`.
- **Lado esquerdo**: as tools. *Terrain* (Select / move, Floor, Walls), *Dungeon* (Entrance, Exit, Checkpoint, Gate, Enemy) e *Markers* (Point, Group point, Area, Line, Route, Sign), e as opções da ferramenta escolhida (tipo de inimigo, sala, tipo de portão, nome do grupo, encaixe, retângulo, muros por tile).
- **Meio**: o mapa. Botão esquerdo usa a ferramenta; botão do meio (ou direito com Select, ou `Espaço`+arrastar) move a visão; a roda dá zoom. `Delete` apaga, `Q`/`E` giram (`Shift` = 45°), setas empurram, `Ctrl+D` duplica, `Ctrl+Z`/`Ctrl+Y` desfazem e refazem, `F` ajusta o mapa à janela, `Esc` volta para Select.
- **Lado direito**: uma aba para cada tipo de dado — *Map* (nome, terreno, território, entrada, saída, chão), *Rooms* (salas, inimigos e ondas), *Units* (tipos de unidade: combate, morte e invocações, fases), *Gates*, *Points* (pontos, grupos, áreas, linhas, valores, rotas, pontos de controle, placas, decoração, prédios e peças de chão) e *Enemy AI* (acinzentada, veja abaixo).
- **View → Game pictures** alterna entre as imagens do jogo e formas simples. **Tools → Edit the file text...** mostra o JSON como será salvo.

## Salvar

Antes de salvar o mapa é conferido (**Tools → Check map** mostra a mesma lista):

- **Erros** (a dungeon não funcionaria): sem chão ou sem peças de chão, chão fora da grade do jogo, sem entrada, sem ponto de saída, nada que termine a dungeon, salas sem nome ou sem área, grupos ou portões apontando para tipo de unidade, sala ou área que não existe, condições erradas (`cleared:`, `death:`, `quest:`), terreno vazio.
- **Avisos** (funciona, mas vale olhar): entrada ou saída fora do chão, salas sem unidades, portões que nunca abrem, sem pontos de controle, unidade fora da área da sala.

Um mapa com erros não é salvo como mapa normal. A janela oferece **Save as draft anyway**, que grava `"rascunho": true` no arquivo para você continuar depois. A marca é só do editor (o servidor não a lê), então um rascunho pode não funcionar no servidor até os erros serem corrigidos. Antes do primeiro salvamento da sessão, o arquivo antigo é copiado para `server\_backup\mapas-<data>`.

## Imagens do jogo

A aba lê `editor-assets\` ao lado do launcher: `ground\<id>.png` (peças de chão), `units\<nome>.png` e `thumbs.json`, mais o `ground_assets.txt` (nomes das peças). Elas são geradas dos assets extraídos do jogo por `tools\editor\build_editor_assets.py`, e o `Build launcher.ps1` copia para `dist\editor-assets`. Sem a pasta o editor funciona do mesmo jeito, com formas e cores lisas. As imagens são *prévias* (forma e cor dos vértices das malhas, não as texturas); unidades sem imagem aparecem como uma letra.

## O editor de IA dos inimigos (futuro)

*Tools → Enemy AI editor...*, a aba *Enemy AI* e o botão *Edit AI...* da aba *Units* estão acinzentados de propósito. O espaço já existe no código (`EnemyAiHook.cs`):

- cada tipo de unidade pode ter um objeto opcional `"ia"`. O editor o mantém intacto ao abrir e salvar um mapa, inclusive em desfazer/refazer e renomeações; o servidor o ignora;
- o editor futuro implementa `IEnemyAiEditor` (`DisplayName`, `Edit(...)`, `Validate(...)`) e se registra em `EnemyAiEditors`; os botões acinzentados se ligam sozinhos e o `Validate` aparece no *Check map*.

## Código

Veja a tabela de arquivos em `README.md`. Para aceitar um campo novo do formato, acrescente um `FieldSpec` na página dona dele (a chave é o nome em português usado no arquivo; chaves com ponto editam objetos aninhados) e uma conferência em `MapValidator.cs` se o servidor precisar. Todo texto passa por `L.T`/`L.F` (ou pelo rótulo/ajuda de um `FieldSpec`, traduzido pelo `JsonForm`), e a tradução vai em `Launcher\lang`.
