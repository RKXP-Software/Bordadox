namespace Bordadox.App;

/// <summary>
/// Todos os atalhos do programa, para a janela Ajuda › Atalhos do teclado. Ao criar ou trocar um atalho,
/// atualize esta lista; o teste <c>ShortcutsTests</c> avisa se um atalho dos menus não estiver aqui.
/// </summary>
public static class Shortcuts
{
    public sealed record Entry(string Group, string Function, string Keys);

    private const string None = "—";

    public static readonly IReadOnlyList<Entry> All = new Entry[]
    {
        // Arquivo
        new("Arquivo", "Novo desenho", "Ctrl+N"),
        new("Arquivo", "Abrir projeto, bordado ou imagem", "Ctrl+O"),
        new("Arquivo", "Salvar projeto", "Ctrl+S"),
        new("Arquivo", "Salvar projeto como", "Ctrl+Shift+S"),
        new("Arquivo", "Exportar para a máquina", "Ctrl+E"),
        new("Arquivo", "Digitalizar imagem (logo ou desenho)", "Ctrl+I"),
        new("Arquivo", "Ficha de produção", None),
        new("Arquivo", "Inserir bordado no desenho", None),
        new("Arquivo", "Imagem de fundo: inserir, mostrar, ajustar, remover", None),
        new("Arquivo", "Sair", "Alt+F4"),

        // Editar
        new("Editar", "Desfazer", "Ctrl+Z"),
        new("Editar", "Refazer", "Ctrl+Y ou Ctrl+Shift+Z"),
        new("Editar", "Copiar", "Ctrl+C"),
        new("Editar", "Colar", "Ctrl+V"),
        new("Editar", "Duplicar", "Ctrl+D"),
        new("Editar", "Excluir", "Del"),
        new("Editar", "Selecionar tudo", "Ctrl+A"),

        // Objeto
        new("Objeto", "Costurar antes (subir na ordem)", "Ctrl+↑"),
        new("Objeto", "Costurar depois (descer na ordem)", "Ctrl+↓"),
        new("Objeto", "Girar 90° horário, anti-horário ou por ângulo", None),
        new("Objeto", "Espelhar horizontal ou vertical", None),
        new("Objeto", "Redimensionar", None),
        new("Objeto", "Centralizar no bastidor", None),
        new("Objeto", "Converter em aplique ou em preenchimento", None),
        new("Objeto", "Explodir (separar em partes)", None),

        // Exibir
        new("Exibir", "Ajustar à tela", "0"),
        new("Exibir", "Tamanho real (1:1)", "1"),
        new("Exibir", "Aumentar zoom", "+"),
        new("Exibir", "Diminuir zoom", "-"),
        new("Exibir", "Zoom pela roda do mouse", "Roda do mouse"),
        new("Exibir", "Bastidor deitado", "B"),
        new("Exibir", "Mover a vista", "Espaço + arrastar, ou botão do meio"),
        new("Exibir", "Grade, saltos de linha, régua", None),

        // Simulação
        new("Simulação", "Reproduzir ou pausar", "P"),
        new("Simulação", "Ir para o início", "Home"),
        new("Simulação", "Ir para o fim", "End"),

        // Ferramentas
        new("Ferramentas", "Selecionar, mover e editar nós", "V"),
        new("Ferramentas", "Ponto corrido", "R"),
        new("Ferramentas", "Coluna satin", "C"),
        new("Ferramentas", "Preenchimento (tatami ou satin)", "F"),
        new("Ferramentas", "Aplique", "A"),
        new("Ferramentas", "Furo no preenchimento", "H"),
        new("Ferramentas", "Texto", "T"),

        // Desenhar (com uma ferramenta de desenho ativa)
        new("Desenhar", "Colocar canto", "Clique esquerdo"),
        new("Desenhar", "Colocar curva", "Clique direito"),
        new("Desenhar", "Restringir a ângulos de 15°", "Shift (segurado)"),
        new("Desenhar", "Concluir objeto", "Enter ou duplo-clique"),
        new("Desenhar", "Cancelar o desenho", "Esc"),
        new("Desenhar", "Desfazer o último ponto", "Backspace"),
        new("Desenhar", "Coluna satin: alternar os dois lados", "Clique alternando A e B"),

        // Selecionar e editar
        new("Selecionar", "Selecionar um objeto ou somar à seleção", "Clique, ou Ctrl/Shift + clique"),
        new("Selecionar", "Selecionar por área", "Arrastar sobre área vazia (Ctrl soma)"),
        new("Selecionar", "Mover a seleção", "Setas (0,5 mm); Shift + setas (5 mm)"),
        new("Selecionar", "Inserir nó no contorno", "Ctrl + clique no contorno"),
        new("Selecionar", "Remover nó", "Duplo-clique no nó"),
        new("Selecionar", "Alternar nó entre canto e curva", "Clique direito no nó"),
        new("Selecionar", "Editar o texto", "Duplo-clique no texto"),
        new("Selecionar", "Limpar a seleção ou voltar à ferramenta Selecionar", "Esc"),

        // Campos e janelas
        new("Campos e janelas", "Ajustar valor numérico em passos", "↑ / ↓"),
        new("Campos e janelas", "Confirmar texto na janela de texto", "Ctrl+Enter"),
        new("Campos e janelas", "Mostrar esta lista", "F1"),
    };
}
