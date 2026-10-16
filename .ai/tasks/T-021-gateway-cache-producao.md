# T-021 — Gateway: modo Produção e cache da vitrine

- **Serviço:** Gateway + Infra
- **Depende de:** T-020
- **Status:** done

## Objetivo
Subir o limite medido no teste de estresse (gargalos: Catalog e gateway) sem adicionar infraestrutura.

## Critérios de aceite
- [x] Compose em `Production`, com migrações automáticas mantidas (`Database__MigrateOnStartup`).
- [x] Sem log por requisição: `Yarp` e `Microsoft.EntityFrameworkCore` em `Warning` (antes: 2 linhas por requisição
      no gateway e cada SQL no Catalog).
- [x] Output cache no gateway só para `GET /api/products*` públicas (5 s, varia por query). Não guarda escrita,
      requisição com login, outros serviços nem health — testes de integração cobrem cada caso.
- [x] E2E tolerante ao cache (recarrega até o estoque novo aparecer).
- [x] Estresse antes/depois registrado em `load/README.md`.

## Notas
- Antes: quebrou aos 1m58s (~1150 req/s pedidas, 49 mil requisições, 3436 descartadas).
- Depois: chegou ao fim (2000 req/s), p95 2,5 ms, 156 mil requisições, nenhuma descartada.
- O cenário de estresse lê só ~21 URLs, então quase tudo vem do cache: é o melhor caso. Tráfego real, com mais
  páginas e produtos diferentes, ganha menos.
- Efeito colateral aceito: estoque/lista na vitrine podem ficar até 5 s atrasados. A compra confere o estoque real.
- Redis só faz sentido com várias cópias do gateway (cache compartilhado) — ver resposta na conversa e I-12.
