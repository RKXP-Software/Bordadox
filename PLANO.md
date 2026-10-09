# Bordadox — Plano de implementação

Software de bordado para uso pessoal, feito do zero (sem engenharia reversa de software comercial).

- **Tecnologia:** C# / .NET 10 / WPF (desktop Windows)
- **Projetos:** `Bordadox.Core` (geometria, pontos, formatos), `Bordadox.App` (interface), `Bordadox.Tests` (xUnit)
- **Máquina:** Brother NQ1400E — saída principal PES (também DST), paleta Brother PEC de 64 cores
- **Bastidores:** 100 × 100, 130 × 180, 160 × 260 mm. O desenho é gravado centralizado.

## Fase 1 — Base e arquivos ✅
1. Modelo de pontos (ponto, salto, corte, troca de cor, fim) em 0,1 mm
2. Leitura/gravação PES (v1 + seção PEC com miniaturas) e DST
3. Testes de ida e volta dos formatos, cabeçalho PEC e bastidores

## Fase 2 — Visualizador ✅ (validado na NQ1400E)
4. Janela principal: tela com zoom (roda), arrastar a vista, grade e bastidor
5. Novo arquivo; abrir PES/DST (menu, Ctrl+O, arrastar e soltar)
6. Simulador de costura (barra + reproduzir/pausar + velocidade)
7. Barra de status: pontos, cores, cortes, tamanho, posição do cursor; lista de cores

## Fase 3 — Editor e digitalização manual ✅ (aguardando teste do usuário)
8. Ferramentas: ponto corrido (simples/triplo), coluna satin (2 trilhos), preenchimento tatami/satin (com furos)
9. Selecionar, mover, editar nós, apagar, girar, espelhar, redimensionar
10. Lista de objetos (ordem de costura) e troca de cor pela paleta Brother
11. Sequenciamento automático: arremates, cortes e saltos
12. Desfazer/refazer; projeto editável `.bdx`
13. Imagem de fundo para digitalizar por cima

Melhorias conhecidas para depois:
- Deslocamentos entre regiões de um mesmo preenchimento ainda podem aparecer por cima (ideal: caminhar por baixo do preenchimento)
- Recuo (inset) do reforço por baixo é aproximado (só na direção das linhas)

## Fase 4 — Letras ✅ (aguardando teste do usuário)
14. Texto com fontes do Windows (satin/tatami), altura, espaçamento, rotação, várias linhas, alinhamento
    - Ferramenta Texto (T): janela com texto, fonte (busca), altura, estilo e prévia; lembra o último uso
    - Duplo-clique num texto reabre a janela
    - Lista de fontes em segundo plano (nomes primeiro, conferência depois) e virtualizada, para quem tem milhares de fontes
    - Letras são preenchimentos (scanline) com furos; os trechos de uma mesma letra se ligam por dentro, sem cortes


## Fase 5 — Avançado

### Etapa 5a — Auto-digitalização de imagem ✅ (concluída)
**Pronto e testado:** Arquivo › Digitalizar imagem (Ctrl+I, ou arrastar/Abrir uma imagem) para PNG, JPG, BMP, GIF e SVG.
Janela com prévia das cores do bordado, tamanho em mm (a largura vale para o desenho, sem a margem), número de cores,
remoção de fundo, detalhe mínimo e tipo de preenchimento. Reduz cores (k-means em Lab), liga cada cor a uma linha Brother,
cria camadas com sobreposição de 0,25 mm, vetoriza (cantos e curvas) e gera preenchimentos tatami/satin.
Aviso de risco com confirmação para fotos e degradês fortes, e para resultados pesados (mais de 300 objetos ou 120 mil pontos).
Núcleo em `src/Bordadox.Core/Digitize`; janela em `src/Bordadox.App/Controls/DigitizeDialog.cs`.

**Traços finos → ponto corrido: PRONTO** (ligado por padrão). Fios de menos de ~1,2 mm viram linha de ponto corrido triplo
pelo centro do traço (`Skeleton.cs`: afinação, poda de espinhos, traçado por ramos); formas grossas continuam preenchidas.
A redução de cores no modo automático passou a usar todos os pixels e descartar as cores "de mistura" das bordas
(`Quantizer.PruneMixtures`), porque um fio fino não tem nenhum pixel "plano" e sua cor se perdia.
Logo de teste: `amostras/beauty time_Prancheta 1-01.png` (borboleta dourada sobre preto) agora sai completo.
A janela de digitalização tem zoom na prévia (roda do mouse, arrastar, botões − + Ajustar).

**Acabamento: PRONTO** (etapa 5a concluída):
1. ✅ Degradê metálico: `ShadeMerger` une tons da mesma família de cor (matiz parecido, encostados e com transição suave na imagem original; a divisa seca entre duas cores chapadas impede a união). Opção "Unir tons do mesmo degradê" na janela.
2. ✅ Cores Brother: a linha mais próxima agora é escolhida por CIEDE2000 (antes, distância em RGB). O dourado do logo Faustino passou de "Marrom claro" para "Latão".
3. ✅ Estimativa de pontos por forma (satin/tatami pela largura, linha fina pelo comprimento do esqueleto): erro de -7% a +2% nos logos de teste (antes, de -55% a +13%).
4. ✅ Histerese: um fio que oscila em torno de 1,2 mm fica todo como linha (trecho grosso só vale se tiver uma parte claramente grossa).
5. ✅ Cruzamentos e bifurcações de linhas finas: `Skeleton.JoinAtJunctions` liga os braços que seguem em frente (2 linhas inteiras em vez de 4 pedaços), e o nó do cruzamento deixou de virar um remendo de preenchimento. Borboleta: 84 → 46 objetos, 54 → 37 cortes.


### Etapa 5d — Qualidade das letras ✅ (aguardando teste do usuário)
- **Satin em colunas** (`ColumnSatin.cs`): a linha central de cada traço vem do esqueleto da letra e os pontos cruzam o traço
  perpendicularmente, com a largura medida por raios até os dois lados do contorno. Quinas vivas (A, V, N, Z, L, E...) e
  cruzamentos viram colunas separadas que se sobrepõem, sem leque de pontos. Letras muito grossas (traço acima de 6 mm), partes
  soltas como o ponto do "i" e qualquer forma cuja cobertura não fecha caem para o preenchimento de antes ("Satin em colunas"
  desligável nas propriedades do texto). As colunas ficam em cache por letra: mover ou girar o texto não recalcula nada.
- **Kerning** com a tabela da própria fonte (medido pelo motor de texto do WPF; fontes que só têm kerning GPOS, como a Georgia,
  não ganham ajuste). Opção "Kerning" nas propriedades do texto.
- **Menos cortes**: `InsideRouter` (A* numa grade) liga os trechos de um preenchimento contornando furos por dentro da forma, em vez
  de saltar e cortar. Vale para letras e para qualquer preenchimento (logos). Cortes nos logos de teste: 112 → 90, 29 → 18, 52 → 43.

Limitações conhecidas das letras:
- Fontes com serifas finas e muito contraste (tipo Times, Didone) ainda saem com algumas colunas curtas sobrepostas; em letras muito
  pequenas (abaixo de uns 5 mm) o tatami continua mais seguro.
- Os pontos de ligação entre colunas ficam por cima do satin (só são cobertos quando caem sob a coluna seguinte).

### Etapa 5b — Aplique ✅ (aguardando teste na máquina)
- **Passos por objeto:** `EmbObject.GenerateSteps` devolve vários `StitchStep` (cor, pontos e parada opcional). O sequenciador troca de
  linha entre os passos, e com `StopBefore` para a máquina mesmo quando a cor é a mesma (entrada repetida na lista de linhas do PES).
- **`AppliqueObject`** (forma fechada com furos, como o preenchimento; base comum `ShapeObject`): 1) linha de posicionamento (corrida
  no contorno), 2) fixação do tecido (zigue-zague ou corrido) com parada antes, 3) acabamento em satin ao longo do contorno
  (`OutlineSatin`: pares perpendiculares, quinas em leque, largura e densidade ajustáveis) com parada antes.
- Ferramenta **Aplique (A)** e **Objeto › Converter em aplique / preenchimento** (mantém contorno, furos, nome, cor e ordem).
- Painel de propriedades com **seletor de cor** (paleta Brother) para a cor da guia.
- Pendente: validar numa bordada real como a NQ1400E trata as paradas (o PES só tem "troca de linha").

### Etapa 5c — Efeitos de preenchimento ✅
- **Degradê** (`FillEffect.Gradient`): as linhas do tatami são divididas entre duas cores por difusão de erro (proporção contínua
  de uma ponta à outra); duas camadas, uma troca de linha. O ângulo do preenchimento gira o degradê; "inverter" troca o sentido.
- **Segue o contorno** (`ContourFill`, Clipper2): anéis concêntricos para dentro (e para fora dos furos), costurados do externo ao interno
  como um espiral contínuo; um anel a meio passo fecha a fresta onde os anéis se encontram.
- **Padrões decorativos** (`PatternFill`): onda, zigue-zague, losangos (linhas vizinhas em oposição de fase) e cruzado (duas grades).
- `FillGenerator` ganhou `KeepRow` (só certas linhas) e `Rows()` (as linhas de costura de uma forma) para os efeitos.

### Pedidos do usuário ainda na fila
- ✅ **Identidade visual (ícone):** bastidor com um X sendo bordado, aprovado pelo usuário; gerado por `build/gerar-icone.ps1` (16 a 256 px, com versão simplificada para tamanhos pequenos). Pendente: tela de abertura e logo para documentos.
- ✅ **Instalador e versão portátil:** `build/publicar.ps1` gera `dist/Bordadox-<versão>-setup.exe` (Inno Setup, por usuário, sem administrador, associa .bdx e "Abrir com" .pes/.dst) e `dist/Bordadox-<versão>-portable.zip`, ambos autocontidos (.NET 10 embutido). Falta assinatura de código.
- ✅ **Digitalizar imagem só como fundo** (sem gerar bordado), para desenhar por cima: caixa "Só usar como fundo" na janela de digitalização; o menu "Imagem de fundo" também aceita SVG.
- ✅ **Régua em mm** (Exibir › Régua): zero no centro do bastidor, marcador do cursor; a escolha é lembrada.
- ✅ **Satin x tatami mais distintos:** o satin tem ângulo automático (pontos cruzam a largura) e o tatami tem penetrações escalonadas também em formas estreitas.
- ✅ **Satin de verdade para letras** (coluna que segue o traço): feito na etapa 5d.

### Demais etapas
15. Digitalização automática de imagem (ver 5a)
16. ✅ Aplique (5b) e efeitos de preenchimento (5c)
17. Formatos JEF/EXP/VP3 e ficha de produção (5e)
18. Acabamento do produto: tela de abertura, logo para documentos, assinatura do instalador

## Regra de trabalho
Ao final de cada fase: compilar, rodar os testes e mostrar o resultado antes de seguir.
