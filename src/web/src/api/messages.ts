import type { FieldErrors } from './client'

type Translation = [field: string, pattern: RegExp, text: string | ((match: RegExpMatchArray) => string)]

/**
 * A API devolve mensagens em inglês (código em inglês). Aqui elas viram texto em português para a interface.
 * O mesmo campo pode significar coisas diferentes por tela (o "name" da conta é o nome da pessoa; o do produto,
 * o nome do produto), por isso há uma tabela por contexto. Mensagem desconhecida passa como veio.
 */
const tables = {
  account: [
    ['email', /required/i, 'Informe seu email.'],
    ['email', /valid email/i, 'Informe um email válido.'],
    ['name', /required/i, 'Informe seu nome.'],
    ['name', /at most (\d+)/i, (m) => `O nome pode ter no máximo ${m[1]} caracteres.`],
    ['password', /required/i, 'Informe sua senha.'],
    ['password', /at least 8/i, 'A senha precisa ter pelo menos 8 caracteres.'],
  ],
  product: [
    ['name', /required/i, 'Informe o nome do produto.'],
    ['name', /at most (\d+)/i, (m) => `O nome pode ter no máximo ${m[1]} caracteres.`],
    ['description', /at most (\d+)/i, (m) => `A descrição pode ter no máximo ${m[1]} caracteres.`],
    ['price', /required/i, 'Informe o preço.'],
    ['price', /greater than zero/i, 'O preço precisa ser maior que zero.'],
    ['price', /2 decimal/i, 'Use no máximo 2 casas decimais.'],
    ['price', /negative/i, 'O preço não pode ser negativo.'],
    ['stock', /negative/i, 'O estoque não pode ser negativo.'],
    ['currency', /./, 'Moeda inválida.'],
  ],
} satisfies Record<string, Translation[]>

export type MessageContext = keyof typeof tables

export function translateFieldErrors(errors: FieldErrors, context: MessageContext = 'account'): FieldErrors {
  const translated: FieldErrors = {}
  for (const [field, message] of Object.entries(errors)) {
    translated[field] = translate(tables[context], field, message)
  }
  return translated
}

function translate(table: Translation[], field: string, message: string): string {
  for (const [f, pattern, text] of table) {
    const match = f === field ? message.match(pattern) : null
    if (match) {
      return typeof text === 'function' ? text(match) : text
    }
  }
  return message
}
