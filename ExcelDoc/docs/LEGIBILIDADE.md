## Diretrizes de legibilidade e formatação de código

Ao gerar, modificar ou refatorar código, priorize **legibilidade, clareza e facilidade de manutenção**, em vez de concisão excessiva.

Siga obrigatoriamente estas convenções:

1. **Uma instrução por linha:** nunca coloque duas ou mais instruções independentes na mesma linha.
2. **Chaves explícitas:** utilize `{ }` em todas as estruturas `if`, `else`, `for`, `foreach`, `while`, `try`, `catch` e `finally`, mesmo quando houver apenas uma instrução.
3. **Espaçamento entre blocos:** utilize linhas em branco para separar operações com responsabilidades diferentes, sem exagerar na quantidade de espaços.
4. **Indentação consistente:** respeite as convenções de formatação da linguagem e do projeto.
5. **Evite código excessivamente compacto:** não comprima condicionais, atribuições ou chamadas de métodos apenas para reduzir o número de linhas.
6. **Priorize clareza sobre quantidade de linhas:** prefira código um pouco mais extenso quando isso facilitar a compreensão.
7. **Evite complexidade desnecessária:** não crie abstrações, métodos auxiliares ou variáveis adicionais apenas para aumentar o número de linhas.
8. **Respeite o estilo existente:** mantenha as convenções do projeto, exceto quando elas conflitarem com estas regras de legibilidade.

**Princípio fundamental:** o código deve ser facilmente compreendido por outro desenvolvedor ao realizar uma leitura rápida, sem precisar interpretar expressões excessivamente compactadas.

Essas diretrizes devem ser aplicadas a todo código gerado ou modificado.