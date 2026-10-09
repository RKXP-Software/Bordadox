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

Limitações conhecidas das letras:
- O satin das letras é por linhas de varredura (ângulo fixo), não por colunas que seguem o traço como no Wilcom
  (letras pequenas, até ~5 mm, ficam boas; letras grandes ficam melhores como tatami)
- Sem kerning entre pares de letras (usa só a largura de cada letra)
- Ainda há cortes nos saltos que atravessam o "buraco" de letras como o, a, B

## Fase 5 — Avançado

### Etapa 5a — Auto-digitalização de imagem (parcial; retomar daqui)
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

**Pendente (nesta ordem):**
1. Degradê metálico (várias tonalidades de dourado) vira camadas separadas; avaliar fundir tons por matiz.
2. Cores Brother: o dourado cai em "Cáqui"/"Marrom claro"; avaliar uma distância de cor melhor que a do PEC (redmean).
3. A estimativa de pontos da janela (1,9 pontos/mm²) subestima logos de linha fina (estimou ~860, o real foi ~1.800).
4. Fios com espessura perto de 1,2 mm podem alternar entre linha e preenchimento ao longo do traço (histerese).
5. Linhas finas que se cruzam saem como vários trechos separados; ligar os trechos numa linha contínua reduziria cortes.

### Pedidos do usuário ainda na fila
- **Identidade visual (no final do projeto):** logo do Bordadox, por exemplo um "X" sendo bordado; ícone do app (.ico) e da janela, e tela de abertura.
- ✅ **Instalador e versão portátil:** `build/publicar.ps1` gera `dist/Bordadox-<versão>-setup.exe` (Inno Setup, por usuário, sem administrador, associa .bdx e "Abrir com" .pes/.dst) e `dist/Bordadox-<versão>-portable.zip`, ambos autocontidos (.NET 10 embutido). Ícone provisório; falta assinatura de código.
- ✅ **Digitalizar imagem só como fundo** (sem gerar bordado), para desenhar por cima: caixa "Só usar como fundo" na janela de digitalização; o menu "Imagem de fundo" também aceita SVG.
- ✅ **Régua em mm** (Exibir › Régua): zero no centro do bastidor, marcador do cursor; a escolha é lembrada.
- ✅ **Satin x tatami mais distintos:** o satin tem ângulo automático (pontos cruzam a largura) e o tatami tem penetrações escalonadas também em formas estreitas.
- **Satin de verdade para letras e traços estreitos** (coluna que segue o traço, via esqueleto): ainda pendente; depende do esqueleto (5a, item 1).

### Demais etapas
15. Digitalização automática de imagem (ver 5a)
16. Aplique, efeitos de preenchimento, formatos JEF/EXP/VP3, ficha de produção

## Regra de trabalho
Ao final de cada fase: compilar, rodar os testes e mostrar o resultado antes de seguir.
