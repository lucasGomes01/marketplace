import { describe, expect, it } from 'vitest'
import { cardNumberError, formatCardNumber, isMastercard, parseExpiry, passesLuhn } from './card'

describe('card number', () => {
  it('groups digits in fours while typing', () => {
    expect(formatCardNumber('5555555555554444')).toBe('5555 5555 5555 4444')
    expect(formatCardNumber('5555-55')).toBe('5555 55')
    expect(formatCardNumber('55555555555544449999')).toBe('5555 5555 5555 4444')
  })

  it.each(['5555555555554444', '5105105105105100', '2223000048400011', '4111111111111111'])('%s passes Luhn', (n) => {
    expect(passesLuhn(n)).toBe(true)
  })

  it('catches a wrong digit', () => {
    expect(passesLuhn('5555555555554445')).toBe(false)
  })

  it.each([
    ['5100000000000000', true],
    ['5599999999999999', true],
    ['2221000000000000', true],
    ['2720999999999999', true],
    ['4111111111111111', false],
    ['5600000000000000', false],
    ['2721000000000000', false],
  ])('%s is Mastercard: %s', (digits, expected) => {
    expect(isMastercard(digits)).toBe(expected)
  })

  it.each([
    ['', 'Informe o número do cartão.'],
    ['5555 5555', 'O cartão precisa ter 16 dígitos.'],
    ['5555 5555 5555 4445', 'Número de cartão inválido. Confira os dígitos.'],
    ['4111 1111 1111 1111', 'Só aceitamos cartões Mastercard para receber.'],
    ['5555 5555 5555 4444', null],
  ])('validates %s', (input, error) => {
    expect(cardNumberError(input)).toBe(error)
  })
})

describe('parseExpiry', () => {
  it.each([
    ['12/29', { month: 12, year: 2029 }],
    ['1/2030', { month: 1, year: 2030 }],
    [' 08 / 31 ', { month: 8, year: 2031 }],
  ])('reads %s', (input, expected) => {
    expect(parseExpiry(input)).toEqual(expected)
  })

  it.each(['', '13/29', '00/29', '1229', '12/2'])('rejects %s', (input) => {
    expect(parseExpiry(input)).toBeNull()
  })
})
