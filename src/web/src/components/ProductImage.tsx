import { useState } from 'react'

/**
 * Foto do produto ou, sem foto (ou se o endereço falhar), a inicial do nome sobre um fundo de cor.
 * O alt fica vazio quando o nome já aparece ao lado (cartão, carrinho).
 */
export function ProductImage({ src, name, alt = '', className }: { src?: string; name: string; alt?: string; className?: string }) {
  const [failedSrc, setFailedSrc] = useState<string | null>(null)
  const classes = `product-image${className ? ` ${className}` : ''}`

  if (!src || failedSrc === src) {
    return (
      <div className={`${classes} product-image--placeholder`} aria-hidden="true">
        {name.charAt(0).toUpperCase()}
      </div>
    )
  }
  return (
    <div className={classes}>
      <img src={src} alt={alt} loading="lazy" decoding="async" onError={() => setFailedSrc(src)} />
    </div>
  )
}
