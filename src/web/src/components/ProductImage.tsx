import { useState } from 'react'
import { Icon } from './Icon'

/**
 * Foto do produto ou, sem foto (ou se o endereço falhar), um espaço neutro com "Sem foto".
 * O alt fica vazio quando o nome já aparece ao lado (cartão, carrinho).
 */
export function ProductImage({ src, alt = '', className }: { src?: string; alt?: string; className?: string }) {
  const [failedSrc, setFailedSrc] = useState<string | null>(null)
  const classes = `product-image${className ? ` ${className}` : ''}`

  if (!src || failedSrc === src) {
    return (
      <div className={`${classes} product-image--placeholder`} aria-hidden="true">
        <Icon name="image" size={28} />
        <span>Sem foto</span>
      </div>
    )
  }
  return (
    <div className={classes}>
      <img src={src} alt={alt} loading="lazy" decoding="async" onError={() => setFailedSrc(src)} />
    </div>
  )
}
