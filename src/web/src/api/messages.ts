import type { FieldErrors } from './client'

/**
 * A API devolve mensagens em inglês (código em inglês). Aqui elas viram texto em português para a interface.
 * Mensagem desconhecida passa como veio, para nunca esconder um erro.
 */
const translations: Array<[field: string, pattern: RegExp, text: string]> = [
  ['email', /required/i, 'Informe seu email.'],
  ['email', /valid email/i, 'Informe um email válido.'],
  ['name', /required/i, 'Informe seu nome.'],
  ['name', /at most/i, 'O nome pode ter no máximo 100 caracteres.'],
  ['password', /required/i, 'Informe sua senha.'],
  ['password', /at least 8/i, 'A senha precisa ter pelo menos 8 caracteres.'],
]

export function translateFieldErrors(errors: FieldErrors): FieldErrors {
  const translated: FieldErrors = {}
  for (const [field, message] of Object.entries(errors)) {
    const match = translations.find(([f, pattern]) => f === field && pattern.test(message))
    translated[field] = match ? match[2] : message
  }
  return translated
}
