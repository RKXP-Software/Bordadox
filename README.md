# Bordadox

Programa de **digitalização de bordados** para Windows, feito do zero e pensado para a **Brother NQ1400E**.
Abre, simula, desenha e edita bordados, escreve textos com as fontes do Windows, transforma um logo
(PNG, JPG, SVG) em bordado e exporta em **PES** (Brother) e **DST** (Tajima).

> Projeto de uso pessoal. Não tem relação com a Wilcom, a Brother ou qualquer outro fabricante e **não** usa código,
> recursos ou engenharia reversa de nenhum software comercial: tudo foi escrito a partir de formatos de arquivo documentados
> publicamente e de técnicas conhecidas de digitalização.

- **Tecnologia:** C# / .NET 10 / WPF (somente Windows).
- **Máquina:** Brother NQ1400E. Bastidores: 100 × 100, 130 × 180 e 160 × 260 mm.
- **Plano e andamento detalhados:** [PLANO.md](PLANO.md).

---

## Sumário
1. [O que já foi feito](#1-o-que-já-foi-feito)
2. [Como rodar](#2-como-rodar)
3. [Como usar, com exemplos](#3-como-usar-com-exemplos)
4. [Atalhos](#4-atalhos)
5. [Como o projeto é organizado](#5-como-o-projeto-é-organizado)
6. [Testes](#6-testes)
7. [Limitações conhecidas](#7-limitações-conhecidas)
8. [O que falta implementar](#8-o-que-falta-implementar)

---

## 1. O que já foi feito

### Arquivos e máquina
- **Abrir e gravar PES e DST.** O PES é gravado no formato que a Brother lê (cabeçalho PES + seção PEC com as miniaturas do visor),
  com a paleta de 64 linhas Brother. O DST não guarda cores (abre com cores de contraste).
  O arquivo gerado foi **testado e aceito pela NQ1400E** (abre, mostra miniatura e cores, corta entre as cores).
- **Projeto editável `.bdx`** (JSON legível): objetos, bastidor, configurações e imagem de fundo ficam no arquivo.
- **Bastidores da NQ1400E** (100×100, 130×180, 160×260) e **bastidor deitado**: a área de trabalho gira, e na exportação o desenho
  é girado 90° para caber no bastidor da máquina, que fica sempre em pé (dá para desligar).

### Visualização
- Zoom (roda do mouse), mover a vista (botão do meio ou Espaço + arrastar), grade, ajustar à tela e tamanho real.
- **Simulador de costura** (reproduzir/pausar, 4 velocidades, barra de progresso, marca da agulha).
- **Régua em milímetros** (Exibir › Régua): zero no centro do bastidor, marcador do cursor, escolha lembrada.
- Pontos desenhados com sombra e brilho quando se amplia; barra de status com pontos, cores, cortes, tamanho e posição em mm.

### Editor
- Ferramentas: **Selecionar**, **Ponto corrido** (simples ou triplo), **Coluna satin** (dois trilhos), **Preenchimento** (tatami ou satin,
  com furos) e **Texto**.
- Cantos e curvas ao desenhar (clique esquerdo = canto, direito = curva), Shift = ângulos de 15°.
- Editar nós (arrastar, inserir, remover, alternar canto/curva), mover, selecionar por área, girar, espelhar, redimensionar,
  centralizar, duplicar, copiar/colar, **desfazer/refazer**, reordenar a sequência de costura e trocar a cor pela paleta Brother.
- **Painel de propriedades** de cada objeto: ângulo, densidade, comprimento do ponto, compensação de puxada, reforço por baixo etc.
- **Satin com ângulo automático** (os pontos cruzam a largura da forma) e **tatami** com penetrações escalonadas.
- **Montagem automática da costura:** pontos de arremate, cortes em saltos longos, trocas de cor e ligação dos trechos
  de um preenchimento por dentro da forma (menos saltos e cortes).
- Imagem de fundo (PNG, JPG, BMP, SVG) com posição, largura e opacidade, para desenhar por cima.

### Texto
- Fontes instaladas no Windows, com **busca** (lista lida em segundo plano e só com fontes que têm letras latinas, para não pesar
  com milhares de fontes), cada nome mostrado na própria fonte.
- Altura das maiúsculas em mm, negrito, itálico, espaço entre letras e linhas, alinhamento, rotação, várias linhas, satin ou tatami.
- Janela de texto com prévia; lembra a última fonte, altura e estilo.

### Digitalizar imagem (logo → bordado)
- Aceita **PNG, JPG, BMP, GIF e SVG** (pelo menu, Ctrl+I ou arrastando o arquivo para a janela).
- Reduz as cores (k-means em Lab), liga cada cor à linha Brother mais próxima, remove o fundo (inclusive o miolo do "O" e do "A"),
  **recorta a margem** (a largura em mm vale para o desenho), cria camadas com uma pequena sobreposição entre cores e vetoriza
  os contornos (cantos e curvas) em preenchimentos tatami/satin.
- **Traços finos** (fios de menos de ~1,2 mm) viram **linha de ponto corrido** pelo centro do traço (esqueleto da linha),
  o que permite logos de linha fina, como uma borboleta dourada sobre fundo preto.
- Janela com **prévia das cores do bordado com zoom**, tamanho, número de cores, nível de detalhe, tipo de preenchimento
  e a opção **"Só usar como fundo"** (a imagem não vira bordado, só fica atrás para você desenhar por cima).
- **Aviso de risco para fotos e degradês fortes** (com confirmação) e novo aviso se o resultado ficar pesado
  (mais de 300 objetos ou 120 mil pontos).

---

## 2. Como rodar

Requisitos: **Windows 10/11** e o **.NET 10 SDK**.

```bash
git clone https://github.com/RKXP-Software/Bordadox.git
cd Bordadox
dotnet run --project src/Bordadox.App
```

Abrir um arquivo direto (também funciona "Abrir com" no Explorer):

```bash
dotnet run --project src/Bordadox.App -- amostras/demo_editor.bdx
```

Gerar os arquivos de exemplo (PES, DST e projetos) e rodar os testes:

```bash
dotnet run --project tools/Bordadox.Amostras -- amostras
dotnet test
```

### Instalar sem o SDK (instalador e versão portátil)

Os pacotes ficam em `dist/` e já trazem o .NET 10 embutido (Windows 10/11 de 64 bits, nada a instalar antes):

- `Bordadox-<versão>-setup.exe`: instalador. Instala só para o usuário atual (não pede administrador) em
  `%LOCALAPPDATA%\Programs\Bordadox`, cria atalho no Menu Iniciar (e, opcional, na Área de Trabalho), associa `.bdx`
  ao Bordadox e o inclui em "Abrir com" para `.pes` e `.dst`. Desinstala pelo Windows, removendo as associações.
- `Bordadox-<versão>-portable.zip`: versão portátil. Extraia em qualquer pasta e execute `Bordadox.App.exe`.

Para gerar os dois (precisa do .NET 10 SDK e do [Inno Setup 6](https://jrsoftware.org/isdl.php)):

```bash
powershell -ExecutionPolicy Bypass -File build/publicar.ps1
```

Opções: `-SemTestes` (pula a suíte), `-SemInstalador` (só o portátil). A versão fica em `Directory.Build.props`.
O ícone (bastidor com um X sendo bordado pela agulha) é gerado por `build/gerar-icone.ps1`; a tela de abertura ainda não existe.

---

## 3. Como usar, com exemplos

### Exemplo 1: ver e simular um bordado pronto
1. **Abrir** (Ctrl+O) e escolha `amostras/teste_nq1400e.pes` (60 × 60 mm, 3 cores), ou arraste o arquivo para a janela.
2. Clique em **▶** (ou tecla **P**) para simular a costura. O painel da direita lista as cores e quantos pontos cada uma tem.
3. Ligue **Exibir › Régua (mm)** e use o zoom para conferir as medidas.

### Exemplo 2: desenhar do zero
1. **Novo** (Ctrl+N) e escolha o bastidor (use **B** para deitá-lo).
2. Escolha uma cor na paleta Brother, pressione **F** (Preencher) e clique os pontos do contorno de uma forma. **Enter** conclui.
   Clique com o botão **direito** onde quiser uma curva.
3. Com **V** (Selecionar), clique na forma: o painel **Propriedades** permite trocar tatami/satin, ângulo e densidade.
4. Para um furo: selecione o preenchimento, pressione **H** e desenhe o contorno do furo.
5. Para uma coluna satin: pressione **C** e clique **alternando os dois lados** da coluna (A, B, A, B...).
6. **Ctrl+S** salva o projeto (`.bdx`). **Ctrl+E** exporta para a máquina (PES ou DST).

Projeto de exemplo com preenchimento com furo, preenchimento satin, coluna satin curva e contorno triplo:
`amostras/demo_editor.bdx`.

### Exemplo 3: escrever um texto
1. Pressione **T** e clique onde o texto deve começar.
2. Na janela, digite o texto (várias linhas com Enter), escolha a fonte (digite para buscar, por exemplo `arial bla`),
   a altura das maiúsculas em mm, negrito/itálico e confirme. Dica: abaixo de 5 mm, prefira fontes simples e fortes.
3. Ajuste espaçamento, alinhamento, rotação e tipo de preenchimento em **Propriedades**.
   **Duplo-clique** no texto reabre a janela para editar o conteúdo.

Exemplo pronto: `amostras/demo_texto.bdx` (quatro textos em fontes e tamanhos diferentes, um girado e um alinhado à direita).

### Exemplo 4: transformar um logo em bordado
1. **Arquivo › Digitalizar imagem** (Ctrl+I) ou arraste um PNG/JPG/SVG para a janela.
2. Confira a **prévia das cores do bordado** (use a roda do mouse para ampliar) e ajuste:
   - **Tamanho** em mm (botão *Ajustar ao bastidor*).
   - **Cores**: automático (une cores parecidas) ou um número fixo. Para degradês, reduza para 3 a 5.
   - **Remover o fundo** e se as áreas internas da cor do fundo viram tecido.
   - **Ignorar** detalhes menores que *X* mm e o **tipo de preenchimento**.
3. **Digitalizar.** Os objetos entram selecionados: arraste para mover o logo todo; um único **Ctrl+Z** desfaz tudo.
4. Retoque o que quiser (trocar uma cor, apagar um pedaço, mudar a densidade) e exporte.

Funciona melhor com **logos e desenhos de cores chapadas**. Para fotos ou degradês fortes aparece um aviso de risco que precisa ser confirmado.

### Exemplo 5: usar a imagem só como fundo
Na janela de digitalização, marque **"Só usar como fundo"**: a imagem inteira vai para trás do desenho, sem gerar nenhum ponto,
e você desenha por cima com as ferramentas. Ajuste posição, largura e opacidade nas Propriedades (clique numa área vazia).
Também dá para usar **Arquivo › Imagem de fundo › Inserir imagem**.

### Exemplo 6: exportar para a NQ1400E
1. Confira na barra de status se há o aviso "Parte do desenho está fora do bastidor".
2. **Ctrl+E**, escolha **PES** e salve. Copie o arquivo para a máquina (USB).
3. Com o **bastidor deitado**, o arquivo sai girado 90°: prenda o tecido com o bastidor de lado (a mensagem lembra disso).

---

## 4. Atalhos

| Atalho | Ação |
|---|---|
| Ctrl+N / Ctrl+O / Ctrl+S / Ctrl+Shift+S | Novo / Abrir / Salvar projeto / Salvar como |
| Ctrl+E | Exportar para a máquina (PES/DST) |
| Ctrl+I | Digitalizar imagem |
| Ctrl+Z / Ctrl+Y | Desfazer / Refazer |
| Ctrl+C / Ctrl+V / Ctrl+D / Del | Copiar / Colar / Duplicar / Excluir |
| V, R, C, F, H, T | Ferramentas: Selecionar, Corrido, Satin (coluna), Preencher, Furo, Texto |
| B | Bastidor deitado |
| Enter / Esc / Backspace | Concluir / Cancelar / Desfazer o último ponto ao desenhar |
| Ctrl+clique no contorno | Inserir nó · duplo-clique no nó remove · clique direito no nó alterna canto/curva |
| Setas (Shift = 5 mm) | Mover a seleção em 0,5 mm |
| Ctrl+↑ / Ctrl+↓ | Costurar antes / depois |
| Roda do mouse · botão do meio ou Espaço+arrastar | Zoom · mover a vista |
| 0 / 1 / + / − | Ajustar à tela / tamanho real / zoom |
| P, Home, End | Simulação: reproduzir/pausar, início, fim |

---

## 5. Como o projeto é organizado

```
src/Bordadox.Core        Núcleo sem interface: geometria, pontos, formatos PES/DST, geradores de ponto
                         (corrido, satin, tatami), objetos do desenho, sequenciador e a digitalização de imagens (Digitize/)
src/Bordadox.App         Interface WPF: tela de desenho, editor, janelas (texto, digitalização), leitura de fontes e imagens
tests/Bordadox.Tests     Testes do núcleo (formatos, geradores, digitalização, texto)
tests/Bordadox.App.Tests Testes do editor e da interface (fontes reais, imagens reais)
tools/Bordadox.Amostras  Gera os arquivos de exemplo da pasta amostras/
amostras/                Exemplos gerados (PES, DST, .bdx)
build/                   Scripts de empacotamento: publicar.ps1, instalador.iss (Inno Setup), gerar-icone.ps1
PLANO.md                 Plano por fases e andamento
```

Fluxo dos pontos: **objetos** (`EmbObject`: corrido, satin, preenchimento, texto, pontos importados) →
**geradores** (`Generators/`) → **sequenciador** (`Design/Sequencer.cs`: arremates, cortes, trocas de cor) →
**`StitchPattern`** (pontos em 0,1 mm) → **formatos** (`Formats/`).

Fluxo da digitalização: imagem → `ImageAnalyzer` (detecta foto) → `AutoDigitizer.Prepare` (recorte, cores, fundo, limpeza) →
camadas → `ContourTracer` + `Vectorizer` (formas) e `Skeleton` (traços finos) → objetos do editor.

---

## 6. Testes

```bash
dotnet test
```

Cobrem leitura/gravação de PES e DST (ida e volta, cabeçalho PEC, e arquivos reais), geradores de ponto, sequenciador, editor
(cliques simulados, nós, desfazer), texto com fontes reais, lista de fontes, redução de cores, traçado de contornos,
vetorização, esqueleto de traços finos, detecção de fotos e digitalização de ponta a ponta com imagens reais.

---

## 7. Limitações conhecidas

- **Validado na máquina:** o arquivo PES (formato, cores, cortes, miniatura). O restante (texto, logos digitalizados, bastidor
  deitado, satin automático) foi conferido em tela e por testes automáticos, mas ainda não bordado fisicamente.
  Recomenda-se bordar um teste em tecido de sobra antes de um trabalho de verdade.
- **Satin de letras:** em colunas que seguem o traço (etapa 5d). Letras bem grossas (traço acima de 6 mm) e partes soltas (ponto do "i") usam o preenchimento por linhas. Em fontes com serifas finas, as colunas curtas das serifas se sobrepõem.
- **Kerning** usa a tabela da fonte pelo WPF; fontes com kerning só em GPOS (ex.: Georgia) saem sem ajuste.
- **Ligações dentro de um preenchimento** que se divide em partes contornam furos por dentro da forma (até ~12 mm); desvios maiores ainda geram um corte.
- **Digitalização:** logos de cores chapadas e de linha fina funcionam; **fotos** viram poucas cores (com aviso). Degradês suaves
  de uma cor só (dourado metálico) viram uma cor; degradês entre cores diferentes continuam em faixas. A paleta Brother tem
  poucas cores, então o dourado vira "Cáqui" ou "Latão" conforme o tom.
- **A estimativa de pontos** da janela é aproximada (erro de uns 10% nos logos de teste; o valor final, depois de gerar, é exato).
- Formatos suportados apenas **PES e DST**.
- Somente **Windows** (WPF).

---

## 8. O que falta implementar

Resumo (detalhes e ordem em [PLANO.md](PLANO.md)):

**Digitalização de imagens (etapa 5a):** concluída (degradê, cores Brother por CIEDE2000, estimativa de pontos, histerese e ligação de linhas que se cruzam).

**Novos recursos (fase 5)**
- **Aplique:** linha de posicionamento, de fixação e acabamento em satin.
- **Efeitos de preenchimento:** degradê, padrões e preenchimento que segue o contorno.
- **Qualidade das letras (etapa 5d):** feita (satin em colunas, kerning e ligações por dentro das formas). Falta testar bordando.
- **Mais formatos:** JEF, EXP e VP3.
- **Ficha de produção** para impressão (cores, ordem, tamanho, número de pontos).

**Acabamento do produto**
- **Identidade visual:** ícone do aplicativo e da janela já feito (bastidor com um X sendo bordado); falta a tela de abertura e uma logo para uso em documentos.
- ~~Instalador e versão portátil~~ (feitos: `build/publicar.ps1`). Falta assinar o instalador (o Windows pode avisar "editor desconhecido").
