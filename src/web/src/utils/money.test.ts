import { describe, expect, it } from 'vitest'
import { formatPrice, parsePrice } from './money'

// Intl usa espaço não separável entre "R$" e o número.
const normalize = (text: string) => text.replace(/\s/g, ' ')

describe('formatPrice', () => {
  it('formats BRL the Brazilian way', () => {
    expect(normalize(formatPrice(1234.5, 'BRL'))).toBe('R$ 1.234,50')
  })

  it('respects the product currency', () => {
    expect(normalize(formatPrice(10, 'USD'))).toBe('US$ 10,00')
  })
})

describe('parsePrice', () => {
  it.each([
    ['199,90', 199.9],
    ['1.234,56', 1234.56],
    ['  89,5 ', 89.5],
    ['249', 249],
    ['R$ 12,30', 12.3],
    ['10.5', 10.5],
  ])('reads %s as %d', (input, expected) => {
    expect(parsePrice(input)).toBe(expected)
  })

  it.each(['', 'abc', '12,3,4', '1,2.3'])('rejects %s', (input) => {
    expect(parsePrice(input)).toBeNull()
  })
})
