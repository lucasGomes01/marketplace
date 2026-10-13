# T-013 — Front: base + telas de cadastro, login e "minha conta"

- **Serviço:** Web (`src/web`)
- **Depende de:** T-004b
- **Status:** done

## Objetivo
Primeiras telas reais, já conectadas à API, e a base que as próximas telas vão reaproveitar
(rotas, cliente HTTP, sessão, layout, testes).

## Base técnica
- Rotas: `react-router` (modo declarativo, `BrowserRouter`).
- Testes: Vitest + Testing Library + `user-event` + **MSW** (mock da API no nível de rede, igual ao real).
- Cliente HTTP em `src/api/`: `apiFetch` tipado; erros viram `ApiError` com `status`, `title` e erros por campo
  (formato `ValidationProblemDetails` do .NET).
- Sessão em `src/auth/`: `AuthProvider` guarda o token no `localStorage`, carrega o usuário em `GET /api/users/me`,
  descarta token expirado.
- Textos da interface em **português**; código em inglês.

## Critérios de aceite
- [x] Cabeçalho em todas as páginas: logo (link para a home) e, conforme a sessão, "Entrar"/"Criar conta" ou o
      nome da pessoa + "Sair".
- [x] `/cadastro`: nome, email, senha e tipo de conta (Comprador/Vendedor). Sucesso → login automático →
      `/minha-conta`. Erros de validação aparecem **embaixo do campo**; email duplicado mostra mensagem clara.
- [x] `/entrar`: email e senha. Sucesso → página de onde a pessoa veio (ou `/minha-conta`). Credencial inválida →
      "Email ou senha inválidos."
- [x] `/minha-conta`: protegida. Sem sessão → redireciona para `/entrar`. Mostra nome, email e tipo de conta.
- [x] "Sair" apaga a sessão e volta para a home.
- [x] Sessão sobrevive ao recarregar a página; token expirado é descartado.
- [x] Botões mostram estado de carregamento e ficam desabilitados durante o envio.
- [x] `npm test` roda no CI.

## Testes esperados
- `api/client.test.ts`: sucesso, `ValidationProblem` → erros por campo, erro sem corpo.
- `auth/session.test.ts`: salvar, ler, expirado → `null`.
- Páginas (com MSW): cadastro feliz, erro por campo, email duplicado; login feliz e inválido; minha conta com e sem
  sessão; sair.

## Fora de escopo
- Telas de produto/carrinho/pedido, recuperação de senha, edição de perfil.

## Notas
- 2026-10-13 (concluída): 22 testes (Vitest + MSW). Verificado no navegador contra a API real (compose): cadastro com erro 400 traduzido, cadastro → login automático → minha conta, recarregar mantém a sessão, senha errada, Sair → home; celular (375px) e tema claro/escuro.
- **Bug encontrado na verificação:** Sair numa rota protegida ia para `/entrar`. O React Router 7 navega como *transition* (baixa prioridade), então o `RequireAuth` via a sessão vazia antes da troca de rota. Solução: o estado anônimo carrega `loggedOut` e o `RequireAuth` manda para a home nesse caso. Teste reforçado para checar a página de destino.
- MSW 3 renomeou `onUnhandledRequest` → `onUnhandledFrame`.
- Mensagens da API em inglês são traduzidas em `src/api/messages.ts` (casamento por regex). Ideia I-09: a API devolver códigos de erro em vez de texto.
- `npm audit fix` aplicado (source-map-js, dependência indireta de dev). `npm test` adicionado ao CI.
- Removidos os arquivos do template do Vite (App.css, logos, hero, icons.svg).
