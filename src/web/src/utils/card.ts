/** Só os dígitos do que a pessoa digitou ("5555 5555-5555 4444" → "5555555555554444"). */
export const cardDigits = (input: string) => input.replace(/\D/g, '')

/** Agrupa de 4 em 4 enquanto digita: "5555555555554444" → "5555 5555 5555 4444" (máx. 16 dígitos). */
export const formatCardNumber = (input: string) =>
  cardDigits(input)
    .slice(0, 16)
    .replace(/(\d{4})(?=\d)/g, '$1 ')

/** Algoritmo de Luhn: o dígito verificador dos cartões. */
export function passesLuhn(digits: string): boolean {
  let sum = 0
  for (let i = 0; i < digits.length; i++) {
    let digit = Number(digits[digits.length - 1 - i])
    if (i % 2 === 1) {
      digit *= 2
      if (digit > 9) digit -= 9
    }
    sum += digit
  }
  return digits.length > 0 && sum % 10 === 0
}

/** Faixas de BIN da Mastercard: 51–55 e 2221–2720. */
export function isMastercard(digits: string): boolean {
  const two = Number(digits.slice(0, 2))
  const four = Number(digits.slice(0, 4))
  return (two >= 51 && two <= 55) || (four >= 2221 && four <= 2720)
}

/** Mensagem de erro do número (a mesma regra do servidor) ou null se está ok. */
export function cardNumberError(input: string): string | null {
  const digits = cardDigits(input)
  if (digits.length === 0) return 'Informe o número do cartão.'
  if (digits.length !== 16) return 'O cartão precisa ter 16 dígitos.'
  if (!passesLuhn(digits)) return 'Número de cartão inválido. Confira os dígitos.'
  if (!isMastercard(digits)) return 'Só aceitamos cartões Mastercard para receber.'
  return null
}

/** Lê "MM/AA" ou "MM/AAAA". Inválido → null. */
export function parseExpiry(input: string): { month: number; year: number } | null {
  const match = input.trim().match(/^(\d{1,2})\s*\/\s*(\d{2}|\d{4})$/)
  if (!match) return null
  const month = Number(match[1])
  const year = match[2].length === 2 ? 2000 + Number(match[2]) : Number(match[2])
  return month >= 1 && month <= 12 ? { month, year } : null
}
