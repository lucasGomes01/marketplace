const formatters = new Map<string, Intl.NumberFormat>()

/** Preço no formato brasileiro, respeitando a moeda do produto (ex.: "R$ 1.234,50", "US$ 10,00"). */
export function formatPrice(amount: number, currency: string): string {
  let formatter = formatters.get(currency)
  if (!formatter) {
    formatter = new Intl.NumberFormat('pt-BR', { style: 'currency', currency })
    formatters.set(currency, formatter)
  }
  return formatter.format(amount)
}

const BRAZILIAN = /^\d{1,3}(\.\d{3})*(,\d+)?$|^\d+(,\d+)?$/
const THOUSANDS_ONLY = /^\d{1,3}(\.\d{3})+$/
const DOT_DECIMAL = /^\d+(\.\d+)?$/

/**
 * Lê um preço digitado por uma pessoa no Brasil: "1.234,56", "199,90", "249", "R$ 12,30".
 * Também aceita ponto decimal ("10.5"). "10.500" (sem vírgula) é lido como milhar. Inválido → null.
 */
export function parsePrice(input: string): number | null {
  const text = input.replace(/R\$/i, '').trim()

  if (text.includes(',')) {
    return BRAZILIAN.test(text) ? Number(text.replaceAll('.', '').replace(',', '.')) : null
  }
  if (THOUSANDS_ONLY.test(text)) {
    return Number(text.replaceAll('.', ''))
  }
  return DOT_DECIMAL.test(text) ? Number(text) : null
}
