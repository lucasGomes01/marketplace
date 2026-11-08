import type { ProductCondition } from '../api/products'

export interface Category {
  code: string
  label: string
  /** Texto curto para o cartão de categoria da página inicial. */
  blurb: string
}

/** Mesmos códigos de `ProductCategories` no Catalog. A ordem é a da vitrine. */
export const CATEGORIES: Category[] = [
  { code: 'eletronicos', label: 'Eletrônicos', blurb: 'Áudio, TV e vídeo' },
  { code: 'celulares', label: 'Celulares', blurb: 'Smartphones e acessórios' },
  { code: 'informatica', label: 'Informática', blurb: 'Notebooks e periféricos' },
  { code: 'casa', label: 'Casa e decoração', blurb: 'Móveis, cama, mesa e banho' },
  { code: 'eletrodomesticos', label: 'Eletrodomésticos', blurb: 'Cozinha e lavanderia' },
  { code: 'ferramentas', label: 'Ferramentas', blurb: 'Elétricas e manuais' },
  { code: 'moda', label: 'Moda', blurb: 'Roupas, calçados e bolsas' },
  { code: 'beleza', label: 'Beleza', blurb: 'Perfumes e cuidados' },
  { code: 'esportes', label: 'Esportes', blurb: 'Fitness e lazer' },
  { code: 'brinquedos', label: 'Brinquedos', blurb: 'Jogos e diversão' },
  { code: 'livros', label: 'Livros', blurb: 'Livros e papelaria' },
  { code: 'automotivo', label: 'Automotivo', blurb: 'Peças e acessórios' },
  { code: 'pet', label: 'Pet shop', blurb: 'Tudo para o seu pet' },
  { code: 'outros', label: 'Outros', blurb: 'Achados diversos' },
]

const byCode = new Map(CATEGORIES.map((c) => [c.code, c]))

export function categoryLabel(code: string | undefined): string | null {
  return code ? (byCode.get(code)?.label ?? null) : null
}

export const CONDITIONS: { value: ProductCondition; label: string }[] = [
  { value: 'New', label: 'Novo' },
  { value: 'Used', label: 'Usado' },
  { value: 'Refurbished', label: 'Recondicionado' },
]

export function conditionLabel(condition: ProductCondition | undefined): string {
  return CONDITIONS.find((c) => c.value === condition)?.label ?? 'Novo'
}
