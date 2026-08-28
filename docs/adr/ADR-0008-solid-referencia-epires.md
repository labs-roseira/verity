# ADR-0008 — SOLID com referência EP.SOLID

**Status:** Aceito

## Contexto

O projeto precisa de um guia de estilo consistente para aplicar SOLID sem
over-engineering.

## Decisão

Adotar o repositório [EduardoPires/SOLID](https://github.com/EduardoPires/SOLID/tree/master/EP.SOLID)
como referência didática: cada princípio tem exemplos de violação e solução. O código
deste projeto segue o padrão das pastas `*.Solucao`.

| Princípio | Aplicação |
|---|---|
| SRP | 1 caso de uso = 1 classe; `Entry` só valida; endpoints finos |
| OCP | Novo tipo de lançamento = novo enum; novo evento = novo handler |
| LSP | Implementações de ports honram o contrato; sem NotSupportedException |
| ISP | Ports granulares (`IEventPublisher` tem 1 método) |
| DIP | Application define ports; Infrastructure implementa; Api compõe via DI |

## Alternativas consideradas

- **SOLID "acadêmico" sem referência prática**: rejeitado — tende a over-engineering.
- **Não documentar referência**: rejeitado — falta de alinhamento entre contribuidores.

## Consequências

**Prós:** guia concreto e verificável; code review objetivo (4 regras de verificação);

**Contras:** referência externa pode mudar (link citado no README e convenções).
